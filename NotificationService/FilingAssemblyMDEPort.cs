// -------------------------------------------------------------------------------------------------------------------------------
// FilingAssemblyMDEPort - Filing Notification Service - City of Fresno
// R.Short - BitLink 08/20/18
//
// Purpose: The NotificationService will process all aSync filing acceptance/reject notifications sent from the NFRC system
//          and will send a json formatted message containing the notification back to ePros via the REST services.
// -------------------------------------------------------------------------------------------------------------------------------

using System;
using System.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Linq;
using System.Xml.Linq;
using System.Xml.XPath;
using eSuite.Utils;
using Serilog;
using eSuite.Utils.Models;
using System.ServiceModel;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NotificationService
{
    [ServiceBehavior(Namespace = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServiceMessagingProfile-Definitions-4.0")]
    public class FilingAssemblyMDEPort : IFilingAssemblyMDEPort
    {
        // 
        // Constructor
        //
        public FilingAssemblyMDEPort()
        {
        }

        /// <summary>
        /// NotifyServiceComplete is an optional API that an EFSP can implement in order to receive notification when one of 
        /// the EFSP's customers has been served. Tyler must enable this API on a per EFSP per site basis.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public NotifyFilingReviewCompleteResponse NotifyFilingReviewComplete(NotifyFilingReviewCompleteRequest request)
        {
            Log.Information("***"); // add gap between log entries
            Log.Information("NotifyFilingReviewComplete request: {0} ; message {1}", request, request.NotifyFilingReviewCompleteRequestMessage);
            
            /*
              if (request.NotifyFilingReviewCompleteRequestMessage != null)
                Log.Information(request.NotifyFilingReviewCompleteRequestMessage?.ToString());
            */

            // Process client response message and forward to ePros API service
            Log.Information("ProcessResponse(request.NotifyFilingReviewCompleteRequestMessage)");

           new NotifyFilingReviewCompleteResponseObj().ProcessResponse(request.NotifyFilingReviewCompleteRequestMessage);
            Log.Information(String.Format("NotifyFilingReviewComplete request complete"));

            // Setup/Return receipt response envelope back to client
            return new NotifyFilingReviewCompleteResponse()
            {
                MessageReceiptMessage = this.GetResponse()
            };
        }

        /// <summary>
        /// Returns response xml
        /// </summary>
        /// <returns>response xml</returns>
        private XElement GetResponse()
        {
            try
            {
                using (Stream manifestResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NotificationService.MessageReceipt.xml"))
                    return XElement.Load(manifestResourceStream);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, String.Format("Exception::GetResponse - Error getting response reciept"));
            }
            return null;
        }
    }

    #region FilingAssemblyMDEPortMessageContracts

    /// <summary>
    /// Message contract parameter for requests
    /// </summary>
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    [System.ServiceModel.MessageContractAttribute(IsWrapped = false)]
    public partial class NotifyFilingReviewCompleteRequest
    {
        [System.ServiceModel.MessageBodyMemberAttribute(Namespace = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", Order = 0)]
        public XElement NotifyFilingReviewCompleteRequestMessage { get; set; }

        public NotifyFilingReviewCompleteRequest()
        {
        }
    }

    /// <summary>
    /// Message contract parameter for responses
    /// </summary>
    [System.Diagnostics.DebuggerStepThroughAttribute()]
    [System.CodeDom.Compiler.GeneratedCodeAttribute("System.ServiceModel", "4.0.0.0")]
    [System.ServiceModel.MessageContractAttribute(IsWrapped = false)]
    public partial class NotifyFilingReviewCompleteResponse
    {
        [System.ServiceModel.MessageBodyMemberAttribute(Namespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:MessageReceiptMessage-4.0", Order = 0)]
        public XElement MessageReceiptMessage;

        public NotifyFilingReviewCompleteResponse()
        {
        }
    }

    #endregion

    #region FilingReviewObjects

    /// <summary>
    /// Review accept/reject filing repsonse object
    /// </summary>
    public class NotifyFilingReviewCompleteResponseObj
    {
        // 
        // Constructor
        //
        public NotifyFilingReviewCompleteResponseObj()
        {
        }

        // 
        // Process file & serve xml notification responses that come later after the filing has
        // been submitted.
        //
        public Boolean ProcessResponse(XElement xml)
        {
            Boolean bRetVal = false;

            String archiveFileDocketId = null;
            try
            {
                // Test for valid inbound message                    
                if ( xml == null )
                    throw new Exception("NotifyFilingReviewComplete inbound xml empty");

                Log.Information("Processing NotifyFilingReviewCompleteRequestMessage");
                Log.Information("XML NotifyFilingReviewCompleteRequestMessage namespaces start");
                // XML NotifyFilingReviewCompleteRequestMessage namespaces
                XNamespace reviewFilingNamespace = @"urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:ReviewFilingCallbackMessage-4.0";
                XNamespace ncNamespace = "http://niem.gov/niem/niem-core/2.0";
                XNamespace ecfNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
                XNamespace jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
                XNamespace crimNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CriminalCase-4.0";
                Log.Information("XML NotifyFilingReviewCompleteRequestMessage namespaces end");
                // Load all response xml fields into responseLog
                String sExceptionFault = null;
                Log.Information("Building responseObj from ReviewCallbackMessage");
                //Log.Information("NFRC: XML {0}", xml);
                var responseObj = (from el in xml.Descendants(reviewFilingNamespace + "ReviewFilingCallbackMessage")
                                   select new FilingResponseObj
                                   {
                                       // Load response class attributes
                                       //caseFilingDate = el?.Element(ncNamespace + "DocumentFiledDate")?.Element(ncNamespace + "DateTime").Value ?? "",
                                       caseFilingDate = el?.Element(ncNamespace + "DocumentFiledDate")?.Element(ncNamespace + "DateRepresentation")?.Value ?? "",
                                       // Search for caseFilingID if DocumentIdentification elements > 1 then "FILINGID" will follow else if single DocumentIdentification no "FILINGID",
                                       // so handle both conditional types.
                                       caseFilingId = el?.Elements(ncNamespace + "DocumentIdentification")?.Where(x => (string)x?.Element(ncNamespace + "IdentificationCategoryText") == "FILINGID")?.
                                                                   Select(x => (string)x.Element(ncNamespace + "IdentificationID"))?.FirstOrDefault() ??
                                                                   el?.Element(ncNamespace + "DocumentIdentification")?.Element(ncNamespace + "IdentificationID")?.Value ?? "",
                                       filingEnvelopeId = el?.Elements(ncNamespace + "DocumentIdentification")?.Where(x => (string)x?.Element(ncNamespace + "IdentificationCategoryText") == "ENVELOPEID")?.Select(x => (string)x?.Element(ncNamespace + "IdentificationID"))?.FirstOrDefault() ?? "",
                                       filingCaseTitleText = el?.Element(@"{urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CriminalCase-4.0}CriminalCase")?.Element(@"{http://niem.gov/niem/niem-core/2.0}CaseTitleText")?.Value ?? "",
                                       organizationId = el?.Element(crimNamespace + "CriminalCase")?.Element(jNamespace + "CaseAugmentation")?.Element(jNamespace + "CaseCourt")
                                                          ?.Element(ncNamespace + "OrganizationIdentification")?.Element(ncNamespace + "IdentificationID")?.Value ?? "",
                                       caseTrackingId = el?.Element(crimNamespace + "CriminalCase")?.Element(ncNamespace + "CaseTrackingID")?.Value ?? "",
                                       caseDocketId = el?.Element(crimNamespace + "CriminalCase")?.Element(ncNamespace + "CaseDocketID")?.Value ?? "",
                                       filingStatusText = el?.Element(ecfNamespace + "FilingStatus")?.Element(ncNamespace + "StatusDescriptionText")?.Value ?? "",
                                       filingStatusCode = el?.Element(ecfNamespace + "FilingStatus")?.Element(ecfNamespace + "FilingStatusCode")?.Value ?? ""
                                       
                                   })?.FirstOrDefault();
                Log.Information("responseObj null check");
                Log.Information("responseObj.caseFilingDate {0}", responseObj?.caseFilingDate);
                Log.Information("responseObj.caseFilingId {0}", responseObj?.caseFilingId);
                Log.Information("responseObj.organizationId {0}", responseObj?.organizationId);
                Log.Information("responseObj.caseTrackingId {0}", responseObj?.caseTrackingId);
                Log.Information("responseObj.caseDocketId {0}", responseObj?.caseDocketId);
                Log.Information("responseObj.filingStatusText {0}", responseObj?.filingStatusText);
                Log.Information("responseObj.filingStatusCode {0}", responseObj?.filingStatusCode);
                if (responseObj != null) // invalid object?
                    responseObj.reviewFilingResponse = false; // indicate async/notification type response message
                else
                    sExceptionFault = "Exception:ReviewFilingNotification - Service error processing ofs notify response filing json";

                Log.Information("Process/Send exception response message to ePros if needed");
                // Process/Send exception response message to ePros if needed
                if (responseObj == null && !String.IsNullOrEmpty(sExceptionFault))  // exception?
                {
                    responseObj = new FilingResponseObj();
                    responseObj.organizationId = ConfigurationManager.AppSettings.Get("courtID") ?? "";
                    responseObj.exception = sExceptionFault ?? "Unknown exception fault";
                }
                else
                    archiveFileDocketId = responseObj?.caseDocketId; // record docketId for file

                Log.Information("Create response Json message for ePros Rule value");
                // Create response Json message for ePros Rule value
                var eProsRespJson = new
                {
                    // Add root node 'rfResponse' to json message
                    rfResponse = new Newtonsoft.Json.Linq.JRaw(Newtonsoft.Json.JsonConvert.SerializeObject(responseObj, Newtonsoft.Json.Formatting.None))
                };
                Log.Information("Create rule response Json message for ePros REST service transport");
                // Create rule response Json message for ePros REST service transport
                var eProsRuleRespJson = new
                {
                    ruleCode = "Interface_OFS_ProcessReviewFilingResponseMessages",
                    inputParams = new
                    {
                        @params = new[]{
                            new
                            {
                                name = "rfResponseJson",
                                value = Newtonsoft.Json.JsonConvert.SerializeObject(eProsRespJson, Newtonsoft.Json.Formatting.None)
                            }
                        }
                    }
                };
                Log.Information("Anonymous interface response Json definition");
                // Anonymous interface response Json definition
                var eSuiteRespDef = new
                {
                    eResponse = new
                    {
                        code = "",
                        status = "",
                        message = new
                        {
                            client = new[] { "" },
                            server = new[] { "" }
                        }
                    }
                };
                Log.Information("Anonymous eSuite REST Service definition");
                // Anonymous eSuite REST Service definition
                var eSuiteRuleDef = new
                {
                    @params = new[]{
                        new{
                            name = "",
                            value = ""
                        }
                    }
                };

                // Serialize response obj into standard json and submit to eSuite REST services
                string eProsjson = Newtonsoft.Json.JsonConvert.SerializeObject(eProsRuleRespJson, Newtonsoft.Json.Formatting.None);
                Log.Information("Sending Ofs notification filing response json to ePros");
                Log.Information(eProsjson);

                var token = eRest.GetUserAuthToken(@ConfigurationManager.AppSettings.Get("eSuiteRestAPI_login"),
                                                   @ConfigurationManager.AppSettings.Get("eSuiteRestAPI_pwd"));
                var request = new eRestRequest(@ConfigurationManager.AppSettings.Get("eSuiteRestAPI_url"), token, eRestRequestTypes.Post, eProsjson);
                var sResults = eRest.SubmitRequest(request);
                Log.Information("Complete");

                // Deserialize eSuite rule REST structure response json
                var eRespRule = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(sResults, eSuiteRuleDef);
                if (eRespRule.@params != null)   // valid rule param containing interface rest message?
                {
                    // Deserialize eSuite REST interface response json
                    var cIntResp = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(eRespRule.@params[0].value, eSuiteRespDef);
                    Log.Information("ePros response json");
                    if (cIntResp.eResponse.code == "200") // no error?
                    {
                        Log.Information(cIntResp.ToString());
                        bRetVal = true;
                    } else // error!
                        Log.Error(cIntResp.ToString());
                    Log.Information("Complete");
                }
            }
            catch (Exception ex)
            {
                Log.Information("Exception message: {0}", ex.Message);
                Log.Fatal(ex, String.Format("Exception::ReviewFilingNotification - Error processing xml response"));
            }
            finally // regardless, this must happen for history tracking
            {
                // Save xml notify message to network, if path is missing it's disabled
                if (xml != null)  // valid XElement?
                {
                    var data = XDocument.Parse(xml.ToString());
                    var path = @ConfigurationManager.AppSettings.Get("notifyReviewFolder");
                    if (!String.IsNullOrEmpty(path))   // not disabled?
                    {
                        String messageId = "Notify";
                        if (!String.IsNullOrEmpty(archiveFileDocketId)) // valid docketId?
                            messageId = String.Format(@"{0}-Notify", archiveFileDocketId);
                        WriteFile(path, messageId, data.ToString(), true);
                    }
                } else
                    Log.Error("Inbound xml message empty, notify message not saved");
            }

            return bRetVal;
        }

        // 
        // Write timestamped file to path specified
        // Returns - true if successful or false if error/exception
        // 
        private bool WriteFile(String to, String id, String data, bool isArchiveMonthly = false)
        {
            bool retVal = false;
            try
            {
                Log.Information("Preparing to write file");

                // Check inputs
                if (String.IsNullOrEmpty(to) || String.IsNullOrEmpty(data)) // invalid?
                {
                    Log.Error("File write error - Input path or data attribute = null");
                    return false;
                }

                // Create file timeStamp and add monthly path if needed
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
                if (isArchiveMonthly) // monthly path?
                    to = string.Format(@"{0}\{1}", to, DateTime.Now.ToString("MMM-dd-yyyy"));

                // Create directory path, if it doesn't exist
                Directory.CreateDirectory(to);

                // Create timestamped filename
                String fileName = string.Format(@"{0}\{1}_{2}.xml", to, timestamp, id);

                Log.Information(string.Format(@"Writing file to {0}", fileName));

                // Write out timestamped file to network
                File.WriteAllText(fileName, data);
                Log.Information("Complete");
                retVal = true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, String.Format("Exception::WriteFile - File write error"));
            }
            return retVal;
        }
    }

    // 
    // Filing Response Class 
    //
    public class FilingResponseObj
    {
        public FilingRespEPros ePros { get; set; }
        public Boolean reviewFilingResponse { get; set; }
        public String caseDocketId { get; set; }
        public String caseTrackingId { get; set; }
        public String caseFilingId { get; set; }
        public String caseFilingIdText { get; set; }
        public String caseFilingDate { get; set; }
        public String organizationId { get; set; }
        public String filingStatusText { get; set; }
        public String filingStatusCode { get; set; }
        public String filingCaseTitleText { get; set; }
        public String filingEnvelopeId { get; set; }
        public List<FilingRespError> statusErrorList { get; set; }
        public String exception { get; set; }

        public FilingResponseObj()
        {
            ePros = new FilingRespEPros
            {
                submitDocRefId = ""
            };

            reviewFilingResponse = true;
            caseDocketId = "";
            caseTrackingId = "";
            caseFilingId = "";
            caseFilingIdText = "FILINGID";
            caseFilingDate = "";
            organizationId = "";
            filingStatusText = "";
            filingStatusCode = "";
            filingCaseTitleText = "";
            filingEnvelopeId = "";
            exception = "";

            statusErrorList = new List<FilingRespError>();
            FilingRespError statusError = new FilingRespError
            {
                statusCode = "0",
                statusText = ""
            };
            statusErrorList.Add(statusError);
        }
    }
    public class FilingRespEPros
    {
        public String submitDocRefId { get; set; }
    }
    public class FilingRespError
    {
        public String statusCode { get; set; }
        public String statusText { get; set; }
    }
    
    #endregion
}
