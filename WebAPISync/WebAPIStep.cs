using SimioAPI;
using SimioAPI.Extensions;
using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RestSharp;

namespace WebAPISync
{
    class WebAPStepDefinition : IStepDefinition
    {
        #region IStepDefinition Members

        /// <summary>
        /// Property returning the full name for this type of step. The name should contain no spaces.
        /// </summary>
        public string Name
        {
            get { return "WebAPICall"; }
        }

        /// <summary>
        /// Property returning a short description of what the step does.
        /// </summary>
        public string Description
        {
            get { return "Description text for the 'WebAPICall' step."; }
        }

        /// <summary>
        /// Property returning an icon to display for the step in the UI.
        /// </summary>
        public System.Drawing.Image Icon
        {
            get { return null; }
        }

        /// <summary>
        /// Property returning a unique static GUID for the step.
        /// </summary>
        public Guid UniqueID
        {
            get { return MY_ID; }
        }
        static readonly Guid MY_ID = new Guid("{6cc5df0b-a45e-45e5-a5d5-b56e6d602742}");

        /// <summary>
        /// Property returning the number of exits out of the step. Can return either 1 or 2.
        /// </summary>
        public int NumberOfExits
        {
            get { return 1; }
        }

        /// <summary>
        /// Method called that defines the property schema for the step.
        /// </summary>
        public void DefineSchema(IPropertyDefinitions schema)
        {
            // Example of how to add a property definition to the step.
            IPropertyDefinition pd;

            pd = schema.AddExpressionProperty("URL", "http:\\localhost");
            pd.DisplayName = "URL";
            pd.Description = "URL";
            pd.Required = true;

            pd = schema.AddStringProperty("Method", "GET");
            pd.DisplayName = "Method";
            pd.Description = "Method";
            pd.Required = true;

            // A repeat group of columns and expression where the data will be written
            IRepeatGroupPropertyDefinition columns = schema.AddRepeatGroupProperty("Headers");
            columns.Description = "Headers.";
            pd = columns.PropertyDefinitions.AddStringProperty("Name", String.Empty);
            pd.Description = "Header Name.";
            pd = columns.PropertyDefinitions.AddExpressionProperty("Value", String.Empty);
            pd.Description = "Header Value.";
            pd = schema.AddExpressionProperty("Message", String.Empty);
            pd.DisplayName = "Message";
            pd.Description = "Message";
            pd.Required = false;
            pd = schema.AddStateProperty("StatusCode");
            pd.Description = "The state where the status will be read into.";
            pd.Required = true;
            pd = schema.AddStateProperty("Response");
            pd.Description = "The state where the response will be read into.";
            pd.Required = true;

        }

        /// <summary>
        /// Method called to create a new instance of this step type to place in a process.
        /// Returns an instance of the class implementing the IStep interface.
        /// </summary>
        public IStep CreateStep(IPropertyReaders properties)
        {
            return new WebAPIStep(properties);
        }

        #endregion
    }

    class WebAPIStep : IStep
    {
        IPropertyReaders _properties;
        IPropertyReader _urlProp;
        IPropertyReader _methodProp;
        IRepeatingPropertyReader _headersProp;
        IPropertyReader _messageProp;
        IPropertyReader _statusCodeProp;
        IPropertyReader _responseProp;

        public WebAPIStep(IPropertyReaders properties)
        {
            _properties = properties;
            _urlProp = (IPropertyReader)_properties.GetProperty("URL");
            _methodProp = (IPropertyReader)_properties.GetProperty("Method"); 
            _headersProp = (IRepeatingPropertyReader)_properties.GetProperty("Headers");
            _messageProp = (IPropertyReader)_properties.GetProperty("Message");
            _statusCodeProp = (IPropertyReader)_properties.GetProperty("StatusCode");
            _responseProp = (IPropertyReader)_properties.GetProperty("Response");
        }

        #region IStep Members

        /// <summary>
        /// Method called when a process token executes the step.
        /// </summary>
        public ExitType Execute(IStepExecutionContext context)
        {
            var urlExpresion = (IExpressionPropertyReader)_urlProp;
            string url = urlExpresion.GetExpressionValue((IExecutionContext)context).ToString();
            string method = _methodProp.GetStringValue(context);
            var messageExpresion = (IExpressionPropertyReader)_messageProp;
            string message = messageExpresion.GetExpressionValue((IExecutionContext)context).ToString();
            int numInRepeatGroups = _headersProp.GetCount(context);

            object[,] paramsArray = new object[numInRepeatGroups, 2];
            string[,] stringArray = new string[numInRepeatGroups, 2];

            // an array of string values from the repeat group's list of strings
            for (int i = 0; i < numInRepeatGroups; i++)
            {
                // The thing returned from GetRow is IDisposable, so we use the using() pattern here
                using (IPropertyReaders headersRow = _headersProp.GetRow(i, context))
                {
                    // Get the string property
                    IPropertyReader column = headersRow.GetProperty("Name");
                    paramsArray[i, 0] = column.GetStringValue(context);
                    IExpressionPropertyReader expressionProp = headersRow.GetProperty("Value") as IExpressionPropertyReader;
                    // Resolve the expression to get the value
                    paramsArray[i, 1] = expressionProp.GetExpressionValue(context);
                }
            }

            try
            {
                // for each parameter                
                for (int i = 0; i < numInRepeatGroups; i++)
                {
                    stringArray[i, 0] = (Convert.ToString(paramsArray[i, 0], CultureInfo.CurrentCulture));
                    double doubleValue = paramsArray[i, 1] is double ? (double)paramsArray[i, 1] : Double.NaN;
                    if (!System.Double.IsNaN(doubleValue))
                    {
                        stringArray[i, 1] = (Convert.ToString(doubleValue, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        DateTime datetimeValue = TryAsDateTime((Convert.ToString(paramsArray[i, 1], CultureInfo.InvariantCulture)));
                        if (datetimeValue > System.DateTime.MinValue)
                        {
                            stringArray[i, 1] = (Convert.ToString(datetimeValue, CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            stringArray[i, 1] = (Convert.ToString(paramsArray[i, 1], CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
            catch (FormatException)
            {
                context.ExecutionInformation.ReportError("Bad headers format provided in WebAPICall step.");
            }

            var client = new RestClient(url);
            client.Timeout = -1;
            var request = new RestRequest((Method)Enum.Parse(typeof(Method), method));
            Dictionary<string, string> headers = new Dictionary<string, string>();
            for (int i = 0; i < numInRepeatGroups; i++)
            {
                request.AddHeader(stringArray[i, 0], stringArray[i, 1]);
                headers.Add(stringArray[i, 0], stringArray[i, 1]);
            }

            if (headers.TryGetValue("Accept", out var acceptValue))
                request.AddHeader("Accept", acceptValue);

            if (headers.TryGetValue("Content-Type", out var contentTypeValue))
                request.AddHeader("Content-Type", contentTypeValue);

            if (request.Method != Method.GET || request.Method != Method.DELETE)
            {
                if (contentTypeValue == null) throw new Exception("Content-Type Header Must Be Populated");
                if (message.Length == 0) throw new Exception("Message Must Be Populated");
                request.AddParameter(contentTypeValue, message, ParameterType.RequestBody);
            }

            IRestResponse response = client.Execute(request);

            IStateProperty statusCodeStateProp = (IStateProperty)_statusCodeProp;
            IState stateStatusState = statusCodeStateProp.GetState(context);
            IStringState stringStatusState = stateStatusState as IStringState;
            stringStatusState.Value = response.StatusCode.ToString();

            IStateProperty responseStateProp = (IStateProperty)_responseProp;
            IState responseState = responseStateProp.GetState(context);
            IStringState stringState = responseState as IStringState;

            if (((request.Method == Method.GET && response.StatusCode != System.Net.HttpStatusCode.OK) || ((request.Method == Method.POST) && response.StatusCode != System.Net.HttpStatusCode.OK && response.StatusCode != System.Net.HttpStatusCode.Created && response.StatusCode != System.Net.HttpStatusCode.NoContent)) || (request.Method != Method.GET && request.Method != Method.POST && response.StatusCode != System.Net.HttpStatusCode.NoContent))
            {
                if (response.ErrorMessage != null) stringState.Value = "StatusCode=" + response.StatusCode.ToString() + ", ErrorMessage=" + response.ErrorMessage;
                else stringState.Value = "StatusCode=" + response.StatusCode.ToString() + ", Content=" + response.Content;
            }
            else
            {
                if (response.Content.Length == 0) stringState.Value = String.Empty;
                else stringState.Value = "{ \"d\" : " + response.Content + "}";
            }

            context.ExecutionInformation.TraceInformation(String.Format("URL : '{0} - Status :'{1}' - Response :'{2}' ", url, response.StatusCode.ToString(), response.Content));

            return ExitType.FirstExit;
        }

        DateTime TryAsDateTime(string rawValue)
        {
            DateTime dt = System.DateTime.MinValue;
            if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt;
            }
            else
            {
                return dt;
            }
        }

        #endregion
    }
}
