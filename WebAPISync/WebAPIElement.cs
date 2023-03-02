using SimioAPI;
using SimioAPI.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Net;
using System.Web;
using System.Xml.Linq;
using System.Data;
using System.Xml;
using Newtonsoft.Json;

namespace WebAPISync
{
    class WebAPIElementDefinition : IElementDefinition
    {
        #region IElementDefinition Members

        /// <summary>
        /// Property returning the full name for this type of element. The name should contain no spaces.
        /// </summary>
        public string Name
        {
            get { return "WebAPI"; }
        }

        /// <summary>
        /// Property returning a short description of what the element does.
        /// </summary>
        public string Description
        {
            get { return "Description text for the 'WebAPI' element."; }
        }

        /// <summary>
        /// Property returning an icon to display for the element in the UI.
        /// </summary>
        public System.Drawing.Image Icon
        {
            get { return null; }
        }

        /// <summary>
        /// Property returning a unique static GUID for the element.
        /// </summary>
        public Guid UniqueID
        {
            get { return MY_ID; }
        }
        public static readonly Guid MY_ID = new Guid("{bd7c14be-c989-44a5-bc0a-7c374ff028d2}");

        IEventDefinition _ed;

        /// <summary>
        /// Method called that defines the property, state, and event schema for the element.
        /// </summary>
        public void DefineSchema(IElementSchema schema)
        {
            // Example of how to add a property definition to the element.
            IPropertyDefinition pd = schema.PropertyDefinitions.AddStringProperty("BaseURL", "http://localhost:54000");
            pd.Description = "The Base URL of the http(s) posts.";
            pd.Required = true;

            pd = schema.PropertyDefinitions.AddBooleanProperty("PersistReceivedMessages");
            pd.DisplayName = "Persist Received Messages";
            pd.Description = "Persist Received Messages to be retreived using WebAPIRetrieveIntoOutputTable step";
            pd.DefaultString = "False";
            pd.Required = true;

            _ed = schema.EventDefinitions.AddEvent("ElementEvent");
            _ed.Description = "An event owned by this element";

            schema.ElementFunctions.AddSimpleStringFunction("GetStringValue", "GetStringValue", new SimioSimpleStringFunc(GetStringValue));
        }

        public string GetStringValue(object element)
        {
            var myElement = element as WebAPIElement;
            return myElement.getStringValue();
        }

        /// <summary>
        /// Method called to add a new instance of this element type to a model.
        /// Returns an instance of the class implementing the IElement interface.
        /// </summary>
        public IElement CreateElement(IElementData data)
        {
            return new WebAPIElement(data);
        }

        #endregion
    }

    class WebAPIElement : IElement
    { 
        IElementData _data;
        String _value = String.Empty;
        public System.IO.TextWriter Output { get; set; }
        IWebHost _webhost;
        Task _runningServer;
        List<string> _receivedMessages = new List<string>();
        bool _persistReceivedMessages = false;

        public WebAPIElement(IElementData data)
        {
            _data = data;
        }

        #region IElement Members

        /// <summary>
        /// Method called when the simulation run is initialized.
        /// </summary>
        public void Initialize()
        {
            IPropertyReader baseURLProp = _data.Properties.GetProperty("BaseURL");
            string baseURL = baseURLProp.GetStringValue(_data.ExecutionContext);

            IPropertyReader persistReceivedMessagesProp = _data.Properties.GetProperty("PersistReceivedMessages");
            _persistReceivedMessages = Convert.ToBoolean(persistReceivedMessagesProp.GetDoubleValue(_data.ExecutionContext));

            _webhost = new WebHostBuilder()
            .UseKestrel()
            .UseUrls(baseURL)            
            .ConfigureServices(s => s.AddRouting())
            .ConfigureLogging((hostingContext, logging) =>
            {
                //logging.AddDebug();
                //logging.AddConsole();
                logging.AddProvider(new MyTextWriterLoggerProvider(() => this.Output));
            })
            .Configure(app =>
            {                
                app.UseRouter(r =>
                {
                    r.MapPost("", (request, response, routeData) =>
                    {
                        StreamReader reader = new StreamReader(request.Body);
                        _value = reader.ReadToEnd();
                        if (_persistReceivedMessages == true)
                        {
                            _receivedMessages.Add(_value);
                        }
                        _data.ExecutionContext.Calendar.ScheduleCurrentEvent(null, (obj) =>
                        {
                            _data.Events["ElementEvent"].Fire();
                        });
                        response.StatusCode = StatusCodes.Status200OK;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();            

            _runningServer = _webhost.StartAsync();            
        }

        public string[,] GetArrayOfMessages(string stylesheet, int numOfColumns, out string[,] stringArray, out int numOfRows)
        {
            var mergedDataSet = new DataSet();
            mergedDataSet.Locale = System.Globalization.CultureInfo.InvariantCulture;
            List<string> requestResults = new List<string>();

;           // if messages exist
            if (_receivedMessages.Count > 0)
            {
                //  local collection to avoid locking of public list
                var receivedMessages = _receivedMessages.ToArray();
                _receivedMessages.Clear();

                foreach (string message in receivedMessages)
                {
                    requestResults.Add(ParseDataToXML(message, out var parseError));
                    if (parseError.Length > 0)
                    {
                        throw new Exception(parseError);
                    }
                }   
            }

            if (requestResults.Count > 0)
            {
                foreach (var requestResult in requestResults)
                {
                    var transformedResult = Simio.Xml.XsltTransform.TransformXmlToDataSet(requestResult, stylesheet, null);
                    if (transformedResult.XmlTransformError != null)
                        throw new Exception(transformedResult.XmlTransformError);
                    if (transformedResult.DataSetLoadError != null)
                        throw new Exception(transformedResult.DataSetLoadError);
                    if (transformedResult.DataSet.Tables.Count > 0) numOfRows = transformedResult.DataSet.Tables[0].Rows.Count;
                    else numOfRows = 0;
                    if (numOfRows > 0)
                    {
                        transformedResult.DataSet.AcceptChanges();
                        if (mergedDataSet.Tables.Count == 0) mergedDataSet.Merge(transformedResult.DataSet);
                        else mergedDataSet.Tables[0].Merge(transformedResult.DataSet.Tables[0]);
                        mergedDataSet.AcceptChanges();
                    }
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(requestResult);
                }
            }

            if (mergedDataSet.Tables.Count == 0 || mergedDataSet.Tables[0].Rows.Count == 0)
            {
                numOfRows = 0;
                stringArray = new string[numOfRows, numOfColumns];
            }
            else
            {
                numOfRows = mergedDataSet.Tables[0].Rows.Count;
                stringArray = new string[numOfRows, numOfColumns];
                int rowNumber = -1;
                foreach (DataRow dataRow in mergedDataSet.Tables[0].Rows)
                {
                    rowNumber++;
                    for (int col = 0; col < mergedDataSet.Tables[0].Columns.Count; col++)
                    {
                        stringArray[rowNumber, col] = dataRow.ItemArray[col].ToString();
                    }
                }
            }

            return stringArray;
        }

        internal static string ParseDataToXML(string responseString, out string responseError)
        {
            responseError = String.Empty;

            // no response
            if (responseString.Length == 0) return responseString;

            bool isXMLResponse = false;
            bool isProbablyJSONObject = false;
            XmlDocument xmlDoc;
            if (responseString.Contains("xml"))
            {
                isXMLResponse = true;
            }
            else
            {
                isProbablyJSONObject = checkIsProbablyJSONObject(responseString);
            }

            if (isXMLResponse)
            {
                return responseString;
            }
            else // Default to assume a JSON response
            {
                xmlDoc = JSONToXMLDoc(responseString, isProbablyJSONObject);
            }

            return xmlDoc.InnerXml;
        }

        internal static bool checkIsProbablyJSONObject(string resultString)
        {
            // We are looking for the first non-whitespace character (and are specifically not Trim()ing here
            //  to eliminate memory allocations on potentially large (we think?) strings
            foreach (var theChar in resultString)
            {
                if (Char.IsWhiteSpace(theChar))
                    continue;

                if (theChar == '{')
                {
                    return true;
                }
                else if (theChar == '<')
                {
                    return false;
                }
                else
                {
                    break;
                }
            }
            return false;
        }

        internal static XmlDocument JSONToXMLDoc(string resultString, bool isProbablyJSONObject)
        {
            XmlDocument xmlDoc;
            resultString = resultString.Replace("@", string.Empty);
            if (isProbablyJSONObject == false)
            {
                var prefix = "{ items: ";
                var postfix = "}";

                using (var combinedReader = new StringReader(prefix)
                                            .Concat(new StringReader(resultString))
                                            .Concat(new StringReader(postfix)))
                {
                    var settings = new JsonSerializerSettings
                    {
                        Converters = { new Newtonsoft.Json.Converters.XmlNodeConverter() { DeserializeRootElementName = "data" } },
                        DateParseHandling = DateParseHandling.None,
                    };
                    using (var jsonReader = new JsonTextReader(combinedReader) { CloseInput = false, DateParseHandling = DateParseHandling.None })
                    {
                        xmlDoc = JsonSerializer.CreateDefault(settings).Deserialize<XmlDocument>(jsonReader);
                    }
                }
            }
            else
            {
                xmlDoc = Newtonsoft.Json.JsonConvert.DeserializeXmlNode(resultString, "data");
            }
            return xmlDoc;
        }

        /// <summary>
        /// Method called when the simulation run is terminating.
        /// </summary>
        public void Shutdown()
        {
            var stoppingTask = _webhost.StopAsync();
            stoppingTask.GetAwaiter().GetResult();
            _runningServer?.Wait(TimeSpan.FromSeconds(10));
        }

        class MyTextWriterLogger : ILogger
        {
            Func<System.IO.TextWriter> Writer { get; }
            public MyTextWriterLogger(Func<System.IO.TextWriter> writer)
            {
                Writer = writer;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                var msg = $"{logLevel.ToString()}: {formatter(state, exception)}";
                Writer?.Invoke()?.WriteLine(msg);
            }
        }

        class MyTextWriterLoggerProvider : ILoggerProvider
        {
            Func<System.IO.TextWriter> Writer { get; }
            public MyTextWriterLoggerProvider(Func<System.IO.TextWriter> writer)
            {
                Writer = writer;
            }

            public ILogger CreateLogger(string categoryName) => new MyTextWriterLogger(Writer);

            public void Dispose() { }
        }
                
        public string getStringValue()
        {
            return _value;
        }    
    }

    #endregion
}

