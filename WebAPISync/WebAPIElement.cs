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
            pd.Description = "The Base URL of the http(s) request.";
            pd.Required = true;

            _ed = schema.EventDefinitions.AddEvent("ElementEvent");
            _ed.Description = "An event owned by this element";

            schema.ElementFunctions.AddSimpleStringFunction("GetStringValue", "GetStringValue", new SimioSimpleStringFunc(GetStringValue));
        }

        public string GetStringValue(object element)
        {
            var myElement = element as WebAPIElement;
            return myElement.getStirngValue();
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
                        _data.ExecutionContext.Calendar.ScheduleCurrentEvent(null, (obj) =>
                        {
                            _data.Events["ElementEvent"].Fire();
                        });
                        // My logic
                        response.StatusCode = StatusCodes.Status200OK;
                        return Task.CompletedTask;
                    });
                });
            })
            .Build();            

            _runningServer = _webhost.StartAsync();            
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
                
        public string getStirngValue()
        {
            return _value;
        }    
    }

    #endregion
}

