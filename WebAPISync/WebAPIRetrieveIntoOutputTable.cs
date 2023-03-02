﻿using System;
using System.Globalization;
using SimioAPI;
using SimioAPI.Extensions;

namespace WebAPISync
{
    public class WebAPIRetrieveIntoOutputTableDef : IStepDefinition
    {
        #region IStepDefinition Members

        /// <summary>
        /// Property returning the full name for this type of step. The name should contain no spaces. 
        /// </summary>
        public string Name
        {
            get { return "WebAPIRetrieveIntoOutputTable"; }
        }

        /// <summary>
        /// Property returning a short description of what the step does.  
        /// </summary>
        public string Description
        {
            get { return "The WebAPIRetrieveIntoOutputTable step is used to read persisted message(s)."; }
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
        static readonly Guid MY_ID = new Guid("{b03739ac-d65b-4c04-ae91-ef5ada8d77e2}");

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
            IPropertyDefinition pd;

            pd = schema.AddElementProperty("WebAPIElement", WebAPIElementDefinition.MY_ID);
            pd.DisplayName = "WebAPIElement";
            pd.Description = "WebAPIElement holding messsge receipts";
            pd.Required = true;    

            pd = schema.AddTableReferenceProperty("DestinationTable");
            pd.DisplayName = "Destination Table";
            pd.Description = "Simio destination table";
            pd.Required = true;

            pd = schema.AddBooleanProperty("ClearRowsBeforeGettingMessages");
            pd.DisplayName = "Clear Rows Before Getting Messages";
            pd.Description = "Clear Rows Before Getting Messages.";
            pd.DefaultString = "True";
            pd.Required = true;

            pd = schema.AddStringProperty("Stylesheet", @"<xsl:stylesheet version=""1.0"" xmlns:xsl=""http://www.w3.org/1999/XSL/Transform"">
    <xsl:template match=""node()|@*"">
      <xsl:copy>
        <xsl:apply-templates select=""node()|@*""/>
      </xsl:copy>
    </xsl:template>
</xsl:stylesheet>");
            pd.DisplayName = "Stylesheet";
            pd.Description = "Stylesheet.";
            pd.Required = true;
        }

        /// <summary>
        /// Method called to create a new instance of this step type to place in a process. 
        /// Returns an instance of the class implementing the IStep interface.
        /// </summary>
        public IStep CreateStep(IPropertyReaders properties)
        {
            return new WebAPIRetrieveIntoOutputTable(properties);
        }

        #endregion
    }

    class WebAPIRetrieveIntoOutputTable : IStep
    {
        IPropertyReaders _props;
        IElementProperty _webAPIElementProp;
        ITableReferencePropertyReader _destinationTableReaderProp;
        IPropertyReader _clearRowsBeforeGettingMessagesProp;
        IPropertyReader _stylesheetTopicProp;

        public WebAPIRetrieveIntoOutputTable(IPropertyReaders properties)
        {
            _props = properties;
            _webAPIElementProp = (IElementProperty)_props.GetProperty("WebAPIElement");
            _destinationTableReaderProp = (ITableReferencePropertyReader)_props.GetProperty("DestinationTable");
            _clearRowsBeforeGettingMessagesProp = (IPropertyReader)_props.GetProperty("ClearRowsBeforeGettingMessages");
            _stylesheetTopicProp = _props.GetProperty("Stylesheet");
        }

        #region IStep Members

        /// <summary>
        /// Method called when a process token executes the step.
        /// </summary>
        public ExitType Execute(IStepExecutionContext context)
        {
            WebAPIElement webAPIElementProp = (WebAPIElement)_webAPIElementProp.GetElement(context);
            ITableRuntimeData sourceTable = _destinationTableReaderProp.GetTableReference(context);
            double clearRowsBeforeGettingMessagesDouble = _clearRowsBeforeGettingMessagesProp.GetDoubleValue(context);
            bool clearRowsBeforeGettingMessages = false;
            if (clearRowsBeforeGettingMessagesDouble > 0) clearRowsBeforeGettingMessages = true;
            if (clearRowsBeforeGettingMessages == true) sourceTable.RemoveAllRows(context);
            String stylesheet = _stylesheetTopicProp.GetStringValue(context);

            int numOfColumns = sourceTable.Table.Columns.Count + sourceTable.Table.StateColumns.Count;

            string[,] parts = webAPIElementProp.GetArrayOfMessages(stylesheet, numOfColumns, out string[,] stringArray, out int numOfRows);

            int numReadIn = 0;

            for (int i = 0; i < numOfRows; i++)
            {
                ITableRuntimeDataRow row = sourceTable.AddRow(context);
                for (int j = 0; j < numOfColumns; j++)
                {
                    // Resolve the property value to get the runtime state
                    IState state = row.States[j];
                    string part = parts[i, j];

                    if (TryAsNumericState(state, part) ||
                        TryAsDateTimeState(state, part) ||
                        TryAsStringState(state, part))
                    {
                        numReadIn++;
                    }
                }
            }

            context.ExecutionInformation.TraceInformation(String.Format("Messages(s) have been read from endpoint URL."));

            // We are done reading, have the token proceed out of the primary exit
            return ExitType.FirstExit;
        }

        bool TryAsNumericState(IState state, string rawValue)
        {
            IRealState realState = state as IRealState;
            if (realState == null)
                return false; // destination state is not a real.

            double d = 0.0;
            if (Double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                realState.Value = d;
                return true;
            }
            else if (String.Compare(rawValue, "True", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                realState.Value = 1.0;
                return true;
            }
            else if (String.Compare(rawValue, "False", StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                realState.Value = 0.0;
                return true;
            }

            return false; // incoming value can't be interpreted as a real.
        }

        bool TryAsDateTimeState(IState state, string rawValue)
        {
            IDateTimeState dateTimeState = state as IDateTimeState;
            if (dateTimeState == null)
                return false; // destination state is not a DateTime.

            DateTime dt;
            if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                dateTimeState.Value = dt;
                return true;
            }

            // If it isn't a DateTime, maybe it is just a number, which we can interpret as hours from start of simulation.
            double d = 0.0;
            if (Double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                state.StateValue = d;
                return true;
            }

            return false;
        }

        bool TryAsStringState(IState state, string rawValue)
        {
            IStringState stringState = state as IStringState;
            if (stringState == null)
                return false; // destination state is not a string.

            // Since all input value are already strings, this is easy.
            stringState.Value = rawValue;
            return true;
        }

        #endregion
    }
}
