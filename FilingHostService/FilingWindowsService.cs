﻿// -------------------------------------------------------------------------------------------------------------------------------
// eSeries Odyssey filing/notification services
// Rev 1.0 by R.Short - BitLink 07/27/18
//  . Initial design
//
// Purpose: The FilingHostService will send all pending ePros filings to the Odyssey EFM filing system with a receipt sent to ePros via the 
//          REST services. The NotificationService will process all aSync filing acceptance/reject notifications sent from the NFRC system
//          and will send a json formatted message containing the notification back to ePros via the REST services.
//
// Rev 1.01 by R.Short - BitLink 10/04/18
//  . Added new serilog interface which is accessable to all classes.
//  . Cleaned up functionalility of all methods to allow for better code execution and error catching/reporting.
//  . Added new application settings for REST service API credentials and IP path settings.
//  . Added fault handler to serviceHost
//  . Fixed problem with configuable polling interval using app.config setting.
//  . Added setup installer for service
//  . Added MoveFile method to handle overwrite conditions.
//  . Added method to handle ofs response messages and EFMClient exceptions to forward to ePros REST services.
//  . Added Response Notification callback URL for reviewFile. 
//  . Added check for valid <eProsCfg> section if missing, filing is rejected.
//  . Added notification response message handling for async messages and connected to ePros REST services.
//  . Cleaned up problems w/ ofs result xml handling.
//  . Change response message linq searches for caseFilingID if DocumentIdentification elements > 1 then "FILINGID" will follow else if single DocumentIdentification no "FILINGID",
//    so need to handle both conditional types.
// Rev 1.02 by R.Short - BitLink 10/31/18
//  . Fixed a response Linq xml query problem where if the DocumentIdentification element was missing it was throwing a null exception.
//  . All submission review messages will be saved to a monthly achived a file on network before sending to ePros.
// Rev 1.03 by R.Short - BitLink 11/1/18
//  . Disabled statute searching code block until OFS confirms its needed.
//  . Removed support for IP intergration since FTP to network share connection will be used instead.
//  . Changed a lot of Mikes original App.confg setting names to be more meaningful. 
// Rev 1.04 by R.Short - BitLink 11/10/18
//  . Fixed problem where reviewFiles were not being moved to the failed folder if the EFM reviewFiling pocess throws an error.
// Rev 1.05 by R.Short - BitLink 11/11/18
//  . Added FilingStatus response JSON fields for notficiation message.
// Rev 1.06 by R.Short - BitLink 11/19/18
//  . Fixed problem where the Odyssey system is placing '\' control delimiters in the error text strings. This will be removed
//    from the entire json message now.
//  . Changed to write response review xml if a file has actually been moved to processed/failed folder.
// Rev 1.08 by R.Short - BitLink 12/24/18
//  . Added new GetCaseList logic to search OFS system for a CaseDocketId to find matching CaseTrackingID for case resubmissions.
// Rev 1.09 by R.Short - BitLink 1/22/19
//  . Changed OFS login/password
// Rev 1.10 by R.Short - BitLink 4/11/19
//  . Changed OFS notification url
//  . Added archive management code to cleanup old log processed/failed filings based on config settings.
// Rev 1.11 by R.Short - BitLink 4/16/19
//  . Added check to file cleanup method to ignore missing folder exceptions.
// Rev 1.12 by R.Short - BitLink 5/22/19
//  . Changed epros rest service endpoint to production server.
// Rev 1.13 by R.Short - BitLink 06/05/19
//  . Changed epros rest service endpoint to 8082 server.
// Rev 1.14-15 by R.Short - BitLink 07/19/19
//  . Added support to handle new Mtom streaming in service for accept/reject notification messages.
// Rev 1.16 by R.Short - BitLink 08/20/19
//  . Changed notification service to support new messageContract w/ tyler. Added message inspector classes to remove xml wrapper tags
//    before the notification service consumes/processes xml request message.
// Rev 1.17 by R.Short - BitLink 08/21/19
//  . Fixed problem where notification service was deleting Log instance. The Log instance is now shared by the FilingHost/NotificationService
// -------------------------------------------------------------------------------------------------------------------------------

using FilingHostService.EFMUserService;
using eSuite.Utils;
using eSuite.Utils.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Schema;
using Serilog;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Xml;

namespace FilingHostService
{
    public partial class FilingWindowsService : ServiceBase
    {
        // Private Attributes
        private String _VERSION = "1.17";
        
        private string _filingQueuePath;
        private string _filingFailedPath;
        private string _filingSuccessPath;
        private string _filingStatutePath;
        private string _zipFolder;
        private string _codeFolder;
        private string _courtID;
        private List<string> _courtLocations;
        private string courtLocation;
        
        public string _hourToCheckCodes { get; private set; }
        public string _minutesFrom { get; private set; }
        public string _minutesTo { get; private set; }
        private System.Timers.Timer _timer;
        private string _pfxFilePath;
        private string _privateKeyPassword;
        private const string _defaultPollingInterval = "15";

        private EFMClient _client;  // create base EMFClient class
        private ServiceHost _cHost = null;
               
        public FilingWindowsService()
        {
            InitializeComponent();
        }

        // 
        // Service startup 
        // Setup/host the ESL WCF service and listen for inbound requests from OFS
        //
        protected override void OnStart(string[] args)
        {
            try
            {
                Log.Information(String.Format(@"eSeries Odyssey Review Filing Service - V{0}", _VERSION));
                _cHost = new ServiceHost(typeof(NotificationService.FilingAssemblyMDEPort));
                //Log.Information("_cHost.Description {0}", _cHost.Description);
                _cHost.Faulted += Host_Faulted;
                _cHost.Open();
                RunSetup();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Exception::OnStart - Error starting service host: {0}-{1}", ex.InnerException, ex.Message);
            }
        }

        // 
        // CheckForOutboundMessages
        // Method to test for OFS review filings queued up to process. If so
        // read in file and process filings via EMFClient methods.
        //
        private void CheckForOutboundMessages()
        {
            var eProsCfg = (dynamic)null; 
            try
            {
                Log.Information("***"); // add gap between log entries
                

                // Test for ePros review filings to pick up in the output queue
                
                var files = Directory.EnumerateFiles(_filingQueuePath);
                if (files.Count() > 0)
                {
                    Log.Information(string.Format("Processing {0} OFS review file(s)", files.Count()));
                    Log.Information("Configuring EFMClient user parameters");

                    // EMFClient request 
                    var request = new AuthenticateRequestType()
                    {
                        Email = ConfigurationManager.AppSettings.Get("ofsEmail"),
                        Password = ConfigurationManager.AppSettings.Get("ofsPassword")
                    };
                    
                    var userResponse = _client.AuthenticateUser(request);
                    AuthenticateResponseType authFilingUserResponse = new AuthenticateResponseType();
                    AuthenticateResponseType authDefaultFilingUserResponse = new AuthenticateResponseType();
                    String authenticatedFilingUser = "";

                    if (userResponse.Error != null && userResponse.Error.ErrorCode != "0")
                    {
                        Log.Error(string.Format("EFM User Response error {0}-{1}", userResponse.Error.ErrorCode, userResponse.Error.ErrorText));
                        return;
                    }


                    
                    List<FilingHostService.EFMFirmService.UserType> userTypeList = new List<FilingHostService.EFMFirmService.UserType>();



                    var firmUser = _client.GetFirmUser(userResponse);
                    var firmUserID = firmUser.User.UserID;
                    Log.Information("181: Firm User LastName: {0} ID: {1} firmUserID: {2}", firmUser.User.LastName, firmUser.User.UserID, firmUserID);

                        // Get EMFClient user ID
                        var user = _client.GetUser(new GetUserRequestType()
                    {
                        UserID = userResponse.UserID
                    }, userResponse);
                    
                    foreach (var file in files)
                    {
                        var fileName = file.Split('\\').Last();
                        Log.Information(string.Format("199: Processing reviewFiling {0}",fileName));
                        String caseTitleText = "";
                        String filingDocumentsGUIDLeadDoc = "";
                        String defendantFullName = "";
                        String caseTrackingId = "";
                        List<CourtCase> caseList = new List<CourtCase>();
                        List<String> filedDocuments = new List<string>();
                        // Submit data to Odyssey soap service
                        try
                        {
                            var xml = XElement.Load(file);

                            // Make sure to set the xsi:nil on the appropriate elements
                            xml = ValidateXml(xml);

                            Log.Information("Reading eProsCfg element section");

                            // Get the eSuite ePros config info xml file, then remove it

                            eProsCfg = (from el in xml.Descendants()
                                        where
                                            el.Name.Namespace == "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0"
                                            && el.Name == string.Format("{{{0}}}{1}", el.Name.Namespace, "eProsCfg")
                                        let children = el.Elements()
                                        let caseNumber = children.Where(c => c.Name.LocalName.ToLower() == "casenumber").FirstOrDefault()
                                        let caseCourtLocation = children.Where(c => c.Name.LocalName.ToLower() == "casecourtlocation").FirstOrDefault()
                                        let caseDocketNumber = children.Where(c => c.Name.LocalName.ToLower() == "casedocketnumber").FirstOrDefault()
                                        let caseTitle = children.Where(c => c.Name.LocalName.ToLower() == "casetitle").FirstOrDefault()
                                        let caseInitialFilingID = children.Where(c => c.Name.LocalName.ToLower() == "caseinitialfilingid").FirstOrDefault()
                                        let filingDocID = children.Where(c => c.Name.LocalName.ToLower() == "filingdocid").FirstOrDefault()
                                        let barNumber = children.Where(c => c.Name.LocalName.ToLower() == "attybarnumber").FirstOrDefault()
                                        let attorneyLastName = children.Where(c => c.Name.LocalName.ToLower() == "attylastname").FirstOrDefault()
                                        let attorneyFirstName = children.Where(c => c.Name.LocalName.ToLower() == "attyfirstname").FirstOrDefault()
                                        let attorneyMiddleName = children.Where(c => c.Name.LocalName.ToLower() == "attymiddlename").FirstOrDefault()
                                        let chargeSequences = children.Where(c => c.Name.LocalName.ToLower() == "missingstatutes").FirstOrDefault()?.Elements()
                                        let prefixes = chargeSequences.Elements().Where(c => c.Name.LocalName.ToLower() == "statutecodeattributes").Elements().Where(c => !string.IsNullOrWhiteSpace(c.Value))
                                        let filerFirstName = children.Where(c => c.Name.LocalName.ToLower() == "filerfirstname").FirstOrDefault()
                                        let filerLastName = children.Where(c => c.Name.LocalName.ToLower() == "filerlastname").FirstOrDefault()
                                        let filerEmail = children.Where(c => c.Name.LocalName.ToLower() == "fileremail").FirstOrDefault()
                                        let filerPassword = children.Where(c => c.Name.LocalName.ToLower() == "filerpassword").FirstOrDefault()
                                        let defaultFilerEmail = children.Where(c => c.Name.LocalName.ToLower() == "defaultfileremail").FirstOrDefault()
                                        let defaultFilerPassword = children.Where(c => c.Name.LocalName.ToLower() == "defaultfilerpassword").FirstOrDefault()
                                        let defendantLastName = children.Where(c => c.Name.LocalName.ToLower() == "defendantlastname").FirstOrDefault()
                                        let defendantFirstName = children.Where(c => c.Name.LocalName.ToLower() == "defendantfirstname").FirstOrDefault()
                                        let defendantMiddleName = children.Where(c => c.Name.LocalName.ToLower() == "defendantmiddlename").FirstOrDefault()
                                        let defendantCaseTrackingID = children.Where(c => c.Name.LocalName.ToLower() == "defendantcasetrackingid").FirstOrDefault()
                                        select new
                                        {
                                            element = el,
                                            caseNumber = caseNumber?.Value,
                                            caseCourtLocation = caseCourtLocation?.Value,
                                            caseDocketNumber = caseDocketNumber?.Value,
                                            caseTitle = caseTitle?.Value,
                                            caseInitialFilingID = caseInitialFilingID?.Value,
                                            barNumber = barNumber?.Value,
                                            attorneyLastName = attorneyLastName?.Value,
                                            attorneyFirstName = attorneyFirstName?.Value,
                                            attorneyMiddleName = attorneyMiddleName?.Value,
                                            filingDocID = filingDocID?.Value,
                                            filerFirstName = filerFirstName?.Value,
                                            filerLastName = filerLastName?.Value,
                                            filerEmail = filerEmail?.Value,
                                            filerPassword = filerPassword?.Value,
                                            defaultFilerEmail = defaultFilerEmail?.Value,
                                            defaultFilerPassword = defaultFilerPassword?.Value,
                                            defendantLastName = defendantLastName?.Value,
                                            defendantFirstName = defendantFirstName?.Value,
                                            defendantMiddleName = defendantMiddleName?.Value,
                                            defendantCaseTrackingID = defendantCaseTrackingID?.Value,
                                            missingStatutes = chargeSequences?
                                                    .Select(x => new StatuteCode()
                                                    {
                                                        SequenceID = x.Attribute("id").Value,
                                                        BaseWord = x.Elements().Where(e => e.Name.LocalName.ToLower() == "statutecodeidentificationword").First().Value,
                                                        Prefixes = prefixes.Select(c => c.Value).ToList(),
                                                        Name = x.Elements().Where(e => e.Name.LocalName.ToLower() == "statutecodeidentificationdesc").First().Value,
                                                        AdditionalStatutes = x.Elements().Where(e => e.Name.LocalName.ToLower() == "additionalstatutes"
                                                            && e.Descendants().Any(q => q.Name.LocalName.ToLower() == "statutecodeidentificationword" && !string.IsNullOrWhiteSpace(q.Value))).Elements()
                                                            .Select(a => new StatuteCode()
                                                            {
                                                                SequenceID = x.Attribute("id").Value,
                                                                BaseWord = a.Elements().Where(e => e.Name.LocalName.ToLower() == "statutecodeidentificationword").First().Value,
                                                                Prefixes = a.Elements().Where(e => e.Name.LocalName.ToLower() == "statutecodeattributes").Elements()
                                                                                    .Where(e => e.Name.LocalName.ToLower() == "statutecodeattributeword"
                                                                                    && !string.IsNullOrWhiteSpace(e.Value))
                                                                                    .Select(e => e.Value).ToList(),
                                                                Name = a.Elements().Where(e => e.Name.LocalName.ToLower() == "statutecodeidentificationdesc").First().Value,
                                                            }).ToList()
                                                    }).ToList()
                                        }).FirstOrDefault();
                            courtLocation = eProsCfg.caseCourtLocation;
                            
                            Log.Information($"Authenticating filerFirstName:{eProsCfg.filerFirstName} filerLastName:{eProsCfg.filerLastName}");
                            Log.Information($"Authenticating defaultFilerEmail:{eProsCfg.defaultFilerEmail}");
                            
                            /**************************FilingUser********************************/

                            if (!string.IsNullOrEmpty(eProsCfg.filerEmail) && !string.IsNullOrEmpty(eProsCfg.filerPassword))
                                {
                                    var authRequestResponse = new AuthenticateRequestType()
                                    {
                                        Email = eProsCfg.filerEmail,
                                        Password = eProsCfg.filerPassword
                                    };
                                    
                                    if (authRequestResponse != null && authRequestResponse.Password != null)
                                    {
                                        authFilingUserResponse = _client.AuthenticateUser(authRequestResponse);
                                        Log.Information($"Filing User Auth: {authFilingUserResponse?.Email}");
                                    }
                                }

                            /**************************DefaultFilingUser********************************/
                            if (!string.IsNullOrEmpty(eProsCfg.defaultFilerEmail) && !string.IsNullOrEmpty(eProsCfg.defaultFilerPassword))
                            {
                                var authDefaultRequestResponse = new AuthenticateRequestType()
                                {
                                    Email = eProsCfg.defaultFilerEmail,
                                    Password = eProsCfg.defaultFilerPassword
                                };

                                if (authDefaultRequestResponse != null && authDefaultRequestResponse.Password != null)
                                {
                                    authDefaultFilingUserResponse = _client.AuthenticateUser(authDefaultRequestResponse);
                                    Log.Information($"Default Filing User Auth: {authDefaultFilingUserResponse?.Email}");
                                }
                            }

                            


                            var getPolicyResponse = _client.GetPolicy(userResponse, courtLocation);
                            Log.Information("263: GetPolicyResponse: {0}", getPolicyResponse);

                            Log.Information("265: Filing Attorney: Bar:{0} First:{1} Last:{2}", eProsCfg.barNumber, eProsCfg.attorneyFirstName, eProsCfg.attorneyLastName);

                            var attorneylist = _client.GetAttorneys(userResponse);
                            String attorneyID = "";
                            List<FilingHostService.EFMFirmService.AttorneyType> systemAttorneyList = new List<FilingHostService.EFMFirmService.AttorneyType>();
                            if (attorneylist?.Attorney != null)
                            {
                                foreach (FilingHostService.EFMFirmService.AttorneyType attorneyType in attorneylist.Attorney)
                                {
                                    systemAttorneyList.Add(attorneyType);
                                }
                            }
                            bool attrExists = systemAttorneyList.Exists(x => x.FirstName == eProsCfg.attorneyFirstName && x.LastName == eProsCfg.attorneyLastName && x.BarNumber == eProsCfg.barNumber);
                            if (attrExists == false)
                            {
                                var firmID = systemAttorneyList?.Find(x => x.BarNumber != null)?.FirmID;
                                var createAttyResponse = _client.CreateAttorney(userResponse, eProsCfg.barNumber, eProsCfg.attorneyFirstName, eProsCfg.attorneyMiddleName, eProsCfg.attorneyLastName, firmID);
                                attorneyID = createAttyResponse.StartsWith("err") ? "error" : createAttyResponse;
                            }
                            else
                            {
                                attorneyID = systemAttorneyList.Find(x => x.BarNumber == eProsCfg.barNumber).AttorneyID;
                            }
                            if (!attorneyID.StartsWith("err"))
                            {
                                Log.Information("Filing Attorney GUID {0}", attorneyID);
                                var participantNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";

                                var caseParticipant = xml.Descendants()
                                                    .Where(x => x.Name == string.Format("{{{0}}}{1}", participantNamespace, "CaseParticipant"))?.FirstOrDefault();
                                
                                if (caseParticipant != null)
                                {
                                    
                                    XElement xElement = caseParticipant.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))?.FirstOrDefault();
                                    if (xElement != null)
                                    {
                                        if (string.IsNullOrEmpty(xElement.Value))
                                        {
                                            xElement.Value = attorneyID;
                                        }
                                    }
                                }
                                
                                foreach (XElement xElement1 in xml.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "FilingAttorneyID")))
                                {
                                    
                                    XElement attorneyIdentificationID = xElement1.Elements().Where(e => e.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))?.FirstOrDefault();
                                    if (string.IsNullOrEmpty(attorneyIdentificationID.Value))
                                    {
                                        attorneyIdentificationID.Value = attorneyID;
                                    }
                                }
                            }
                            else
                            {
                                var defaultAttorneyID = systemAttorneyList.Find(x => x.BarNumber != null).AttorneyID;
                                var participantNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
                                
                                var caseParticipant = xml.Descendants()
                                                    .Where(x => x.Name == string.Format("{{{0}}}{1}", participantNamespace, "CaseParticipant"))?.FirstOrDefault();
                                
                                if (caseParticipant != null)
                                {
                                    XElement xElement = caseParticipant.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))?.FirstOrDefault();
                                    if (xElement != null)
                                    {
                                        xElement.Value = defaultAttorneyID;
                                    }
                                }
                                
                                foreach (XElement xElement1 in xml.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "FilingAttorneyID")))
                                {
                                    
                                    XElement attorneyIdentificationID = xElement1.Elements().Where(e => e.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))?.FirstOrDefault();
                                    attorneyIdentificationID.Value = defaultAttorneyID;
                                }
                            }
                            
                            List<EFMFirmService.ServiceContactType> serviceContacts = new List<EFMFirmService.ServiceContactType>();
                            if (_client?.GetContactList(userResponse)?.ServiceContact != null)
                            {
                                foreach (FilingHostService.EFMFirmService.ServiceContactType serviceContactType in _client?.GetContactList(userResponse)?.ServiceContact)
                                {
                                    serviceContacts.Add(serviceContactType);
                                }
                            }
                            var filedContacts = xml.Descendants()
                                 .Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "eProsCfg"))?.FirstOrDefault();
                            var svcContacts = filedContacts.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcContact"));
                            
                            List<EFMFirmService.ServiceContactType> publicServiceContacts = new List<EFMFirmService.ServiceContactType>();
                            List<EFMFirmService.ServiceContactType> attachServiceContacts = new List<EFMFirmService.ServiceContactType>();
                            foreach (XElement contact in svcContacts)
                            {
                                var svcFirstName = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcFirstName"))?.FirstOrDefault()?.Value;
                                var svcMiddleName = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcMiddleName"))?.FirstOrDefault()?.Value;
                                var svcLastName = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcLastName"))?.FirstOrDefault()?.Value;
                                var svcPhoneNumber = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcPhoneNumber"))?.FirstOrDefault()?.Value;
                                var svcEmail = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcEmail"))?.FirstOrDefault()?.Value;
                                var svcAddress1 = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcAddress1"))?.FirstOrDefault()?.Value;
                                var svcAddress2 = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcAddress2"))?.FirstOrDefault()?.Value;
                                var svcCity = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcCity"))?.FirstOrDefault()?.Value;
                                var svcState = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcState"))?.FirstOrDefault()?.Value;
                                var svcZip = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcZip"))?.FirstOrDefault()?.Value ?? "";
                                var svcIsPublic = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcIsPublic"))?.FirstOrDefault()?.Value;
                                var svcAdminCopy = contact.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "svcAdminCopy"))?.FirstOrDefault()?.Value;
                                var firmID = systemAttorneyList.Find(x => x.BarNumber != null).FirmID;
                                
                                svcMiddleName = string.IsNullOrEmpty(svcMiddleName) ? "" : svcMiddleName;
                                Log.Information("Contact FML: {0} {1} {2} Address1: {3} Email: {4}", svcFirstName, svcMiddleName, svcLastName, svcAddress1, svcEmail);
                                var findContact = serviceContacts.Find(sc => sc.FirstName == svcFirstName && sc.LastName == svcLastName && sc.Email == svcEmail && sc.Address.AddressLine1 == svcAddress1 && sc.Address.ZipCode == svcZip);
                                String svcContactID;
                                var getPublicListResponse = _client.GetPublicList(userResponse, svcEmail, svcFirstName, svcLastName, "");

                                if (getPublicListResponse?.ServiceContact != null)
                                {
                                    foreach (var serviceContactType in getPublicListResponse?.ServiceContact)
                                    {
                                        publicServiceContacts.Add(serviceContactType);
                                    }
                                }
                                if (publicServiceContacts.Count > 0)
                                {
                                    findContact = publicServiceContacts.Find(sc => sc.Email == svcEmail);
                                }
                                if (findContact == null)
                                {
                                    svcContactID = _client.CreateServiceContact(userResponse, svcFirstName, svcMiddleName, svcLastName, svcPhoneNumber, svcEmail, svcAddress1, svcAddress2, svcCity, svcZip, svcState, svcIsPublic, svcAdminCopy, firmID);
                                    var getContactResponse = _client.GetServiceContact(userResponse, svcContactID);
                                   
                                }
                                else
                                {
                                    attachServiceContacts.Add(findContact);
                                    svcContactID = findContact.ServiceContactID;
                                }
                                if (!svcContactID.StartsWith("ErrorText:") && svcMiddleName != "test")
                                {
                                    var svcInformation = xml.Descendants()
                                         .Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "ElectronicServiceInformation"));
                                    var identificationID = svcInformation.Descendants().Where(e => e.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID") && string.IsNullOrEmpty(e.Value))?.FirstOrDefault();
                                    if (identificationID != null)
                                    {
                                        identificationID.Value = svcContactID;
                                    }
                                }
                            }

                            _client.GetPaymentAccountTypeList(userResponse);

                            List<EFMFirmService.PaymentAccountType> paymentAccounts = new List<EFMFirmService.PaymentAccountType>();
                            var pmtTypes = _client.GetPaymentAccountList(userResponse);
                            foreach (EFMFirmService.PaymentAccountType paymentAccountType in pmtTypes.PaymentAccount)
                            {
                                paymentAccounts.Add(paymentAccountType);
                            }
                            Log.Information("Filing Court: eProsCfg.caseCourtLocation: {0}", eProsCfg.caseCourtLocation);

                            XElement paymentID = xml.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2", "PaymentID"))?.FirstOrDefault();
                            paymentID.Value = paymentAccounts.Find(x => x.PaymentAccountID != null).PaymentAccountID;
                            
                            if (eProsCfg == null) // missing?
                            {
                                throw new InvalidOperationException(String.Format("Invalid OFS filing, missing <eProsCfg> element section in {0}", file));
                            }
                            else 
                            {
                                Log.Information("Complete, removing eProsCfg section from filing");
                                eProsCfg.element.Remove();
                            }

                            var filingQueryParams = filedContacts.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0", "filingQueryParams"))?.FirstOrDefault();
                            

                            
                            if (!string.IsNullOrWhiteSpace(eProsCfg?.caseDocketNumber)) // valid CaseDocketNumber?
                            {
                                Log.Information(string.Format(@"test-CaseDocketNbr({0}) found in xml- quering caseTrackingID from OFS", eProsCfg.caseDocketNumber));
                                //Get Defendant full name
                                var entityPerson_ = xml.Descendants(@"{urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0}EntityPerson");
                                var element_ = entityPerson_?.Where(attr => attr.Attribute("{http://niem.gov/niem/structures/2.0}id")?.Value == "DEF");
                                var personName_ = element_?.FirstOrDefault()?.Element("{http://niem.gov/niem/niem-core/2.0}PersonName");
                                var firstName_ = personName_?.Element("{http://niem.gov/niem/niem-core/2.0}PersonGivenName")?.Value ?? "";
                                var middleName_ = personName_?.Element("{http://niem.gov/niem/niem-core/2.0}PersonMiddleName")?.Value ?? "";
                                var lastName_ = personName_?.Element("{http://niem.gov/niem/niem-core/2.0}PersonSurName")?.Value ?? "";
                                defendantFullName = String.Format("{0} {1} {2}", firstName_, middleName_, lastName_) ?? "";
                                System.Xml.Linq.XElement getCaseListResponse = _client.GetCaseList(userResponse, courtLocation, eProsCfg.caseDocketNumber);
                                Log.Information("GetCaseList: location: {0}; courtNumber: {1}", courtLocation, eProsCfg.caseDocketNumber);
                                Log.Information("GetCaseList response: {0}", getCaseListResponse);
                                
                                foreach (var caseItem in getCaseListResponse.Descendants().Where(x => x.Name.LocalName.Equals("CriminalCase") || x.Name.LocalName.Equals("CivilCase")).ToList())
                                {
                                    CourtCase courtCase = new CourtCase();
                                    courtCase.caseTitleText = caseItem?.Descendants()?.Where(x => x.Name.LocalName.Equals("CaseTitleText"))?.FirstOrDefault()?.Value ?? "";
                                    courtCase.caseDocketID = caseItem?.Descendants()?.Where(x => x.Name.LocalName.Equals("CaseDocketID"))?.FirstOrDefault()?.Value ?? "";
                                    courtCase.caseTrackingID = caseItem?.Descendants()?.Where(x => x.Name.LocalName.Equals("CaseTrackingID"))?.FirstOrDefault()?.Value ?? "";
                                    caseList.Add(courtCase);
                                }
                                
                                
                                
                                Log.Information($"caseListCount: {caseList.Count()}");
                                IEnumerable<XElement> caseResponse;
                                XElement filteredCriminalCaseResponse;
                                if (!string.IsNullOrEmpty(eProsCfg.caseTitle) || !string.IsNullOrEmpty(eProsCfg.defendantCaseTrackingID) || (!string.IsNullOrEmpty(eProsCfg.defendantLastName) && !string.IsNullOrEmpty(eProsCfg.defendantFirstName)))
                                {
                                    Log.Information($"defendant first:{eProsCfg.defendantFirstName} middle:{eProsCfg.defendantMiddleName} last:{eProsCfg.defendantLastName}");
                                    Log.Information($"defendantCaseTrackingId:{eProsCfg.defendantCaseTrackingID}");
                                    caseResponse = getCaseListResponse.Descendants().Where(x =>
                                    x.Name.LocalName == "CaseTrackingID" && x.Value.Equals(eProsCfg.defendantCaseTrackingID) ||
                                    x.Name.LocalName == "CaseTitleText" && x.Value == eProsCfg.caseTitle ||                                    
                                    x.Name.LocalName == "CaseTitleText" && x.Value.Contains(eProsCfg.defendantLastName) && x.Value.Contains(eProsCfg.defendantFirstName) 
                                    );
                                    filteredCriminalCaseResponse = caseResponse?.FirstOrDefault()?.Parent;
                                    Log.Information($"filteredCriminalCaseResponse: {filteredCriminalCaseResponse}");
                                    caseTitleText = filteredCriminalCaseResponse?.Descendants()?.Where(x => x.Name.LocalName.Equals("CaseTitleText"))?.FirstOrDefault()?.Value ?? "";
                                    caseTrackingId = filteredCriminalCaseResponse?.Descendants()?.Where(x => x.Name.LocalName.Equals("CaseTrackingID"))?.FirstOrDefault()?.Value ?? "";
                                    if (string.IsNullOrEmpty(caseTitleText))
                                    {
                                        Log.Information("Unable to match defendant case");
                                        throw new Exception("Unable to match defendant case");
                                    }
                                    Log.Information($"caseTitleText:{caseTitleText} caseTrackingID:{caseTrackingId}");
                                }
                                
                                Log.Information("caseTrackingId {0} isNullOrEmpty: {1}", caseTrackingId, string.IsNullOrWhiteSpace(caseTrackingId));
                                System.Xml.Linq.XElement getCaseResponse = _client.GetCase(userResponse, courtLocation, caseTrackingId, true);
                                Log.Information("GetCase response: {0}", getCaseResponse);
                                //Log.Information("GetCaseResponse: CaseAugmentation: {0} ", getCaseResponse.Descendants()
                                //    .Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:tyler:ecf:extensions:Common", "CaseAugmentation")));
                                var caseResponseParticipants = getCaseResponse.Descendants()
                                    .Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "CaseParticipant"));
                                XElement defParticipant;
                                String defParticipantIDTest = "";
                                Log.Information("for loop: participants");
                                foreach (XElement par in caseResponseParticipants)
                                {
                                    String participantAttribute = par?.Element(string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "EntityPerson"))?.Attribute("{http://niem.gov/niem/structures/2.0}id")?.Value ?? "";
                                    if (!string.IsNullOrEmpty(participantAttribute) && participantAttribute.StartsWith("Party") && par.Descendants().Where(p1 => (p1.Name.LocalName == "PersonGivenName" && !string.IsNullOrWhiteSpace(p1.Value))).Count() > 0 && string.IsNullOrEmpty(defParticipantIDTest))
                                    {
                                        defParticipant = par;
                                        var defParticipantEntityPerson = defParticipant.Descendants().Where(x => x.Name.LocalName == "EntityPerson")?.FirstOrDefault();
                                        
                                        var defParticipantPersonName = defParticipantEntityPerson?.Element("{http://niem.gov/niem/niem-core/2.0}PersonName");
                                        var defParticipantFirstName = defParticipantPersonName?.Element("{http://niem.gov/niem/niem-core/2.0}PersonGivenName")?.Value ?? "";
                                        var defParticipantMiddleName = defParticipantPersonName?.Element("{http://niem.gov/niem/niem-core/2.0}PersonMiddleName")?.Value ?? "";
                                        var defParticipantLastName = defParticipantPersonName?.Element("{http://niem.gov/niem/niem-core/2.0}PersonSurName")?.Value ?? "";
                                        
                                        defendantFullName = $"{defParticipantLastName}, {defParticipantMiddleName} {defParticipantFirstName}";
                                        Log.Information($"DefParticipant: {defParticipant}; defendantFullName: {defendantFullName}");
                                        var defParticipantID = defParticipant.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault()?.Value ?? "";
                                        XElement filingParty = xml.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "FilingPartyID"))?.FirstOrDefault();
                                        XElement filingPartyID = filingParty?.Descendants()?.Where(x => x.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))?.FirstOrDefault();



                                        
                                        if (!string.IsNullOrEmpty(defParticipantID))
                                        {
                                            defParticipantIDTest = defParticipantID;
                                            
                                            foreach (XElement xElement1 in xml.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0", "FilingPartyID")))
                                            {
                                                
                                                XElement partyIdentificationID = xElement1.Elements().Where(e => e.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))?.FirstOrDefault();
                                                XElement partyIdentificationCategoryText = xElement1.Elements().Where(e => e.Name == string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationCategoryText"))?.FirstOrDefault();
                                                partyIdentificationID.Value = defParticipantID;
                                                partyIdentificationCategoryText.Value = "IDENTIFICATION";
                                            }
                                            
                                        }
                                    }

                                }


                                Log.Information("Case Tracking ID Null Check follows");
                                Log.Information("IsNullOrWhiteSpac ? {0}", string.IsNullOrWhiteSpace(caseTrackingId));

                                if (string.IsNullOrWhiteSpace(caseTrackingId)) // invalid?
                                {
                                    Log.Information("SendEProsResponseMessage:1");
                                    String errMsg = string.Format(@"OFS GetCaseList error - could not find matching caseTrackingID for caseDocketNbr({0})", eProsCfg.caseDocketNumber);
                                    Log.Error(errMsg);
                                    
                                    // Send ePros exception fault response message and move file to failed folder
                                    SendEProsResponseMessage("", courtLocation,eProsCfg?.filingDocID, null, errMsg, "", filedDocuments, "", caseList, "");
                                    MoveFile(file, string.Format(@"{0}\{1}", _filingFailedPath, fileName));
                                    continue;
                                }
                                else // caseTrackingId found, insert it
                                {
                                    Log.Information(string.Format(@"CaseTrackingId({0}) ref found for CaseDocketNbr({1}) - updating filing xml", caseTrackingId, eProsCfg.caseDocketNumber));
                                    Log.Information("585: Certification: attachServiceContacts count : {0}", attachServiceContacts.Count);
                                    foreach (EFMFirmService.ServiceContactType serviceContactType in attachServiceContacts.FindAll(x => string.IsNullOrEmpty(x.MiddleName) || x.MiddleName != "test"))
                                    {
                                        _client.AttachServiceContact(userResponse, caseTrackingId, "", serviceContactType.ServiceContactID);
                                        if (@ConfigurationManager.AppSettings.Get("attachDetachAttachSvcContactTest") == "true")
                                        {
                                            Log.Information("Detach Service Contace");
                                            _client.DetachServiceContact(userResponse, caseTrackingId, "", serviceContactType.ServiceContactID);
                                        }
                                    }
                                    
                                    var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
                                    var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
                                    var caseElm = xml.Descendants()
                                                        .Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseLineageCase"))?.FirstOrDefault();
                                    var caseTrackingElm = caseElm.Elements()
                                                        .Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "CaseTrackingID"))?.FirstOrDefault();
                                    if (caseTrackingElm != null)
                                    {
                                        caseTrackingElm.Value = caseTrackingId;
                                    }
                                    else
                                        throw new InvalidOperationException("Xml error - could not find <CaseTrackingID> section in xml filing to insert caseTrackingId");
                                }
                            }

                            /* Code not used currently by OFS
                            // Search for valid OFS Statutes in OFS database based on statute list returned
                            // via eProsCfg section
                            // 
                            Log.Information("Processing eProsCfg statute codes if available");
                            var codes = _client.GetStatuteCodes(fileName, _courtID, eProsCfg.missingStatutes);
                            if (codes != null && codes.Count > 0)
                            {
                                var charges = (from el in xml.Descendants()
                                               where el.Name.Namespace == "urn:tyler:ecf:extensions:Criminal"
                                               && el.Name == string.Format("{{{0}}}{1}", el.Name.Namespace, "Charge")
                                               select el);

                                foreach (var code in codes)
                                {
                                    var charge = (from c in charges
                                                  let sequence = c.Elements().Where(e => e.Name.LocalName == "ChargeSequenceID").FirstOrDefault()
                                                  where sequence.Element(string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID")).Value == code.SequenceID
                                                  select c).FirstOrDefault();

                                    if (charge != null)
                                    {
                                        charge
                                            .Descendants(string.Format("{{{0}}}{1}", "urn:tyler:ecf:extensions:Criminal", "ChargeStatute"))
                                            .Elements(string.Format("{{{0}}}{1}", "http://niem.gov/niem/domains/jxdm/4.0", "StatuteCodeIdentification"))
                                            .Elements(string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))
                                            .FirstOrDefault().Value = code.Code.ToString();
                                    }
                                }
                            }

                            // This will need to be enabled if required.
                            // var statusCodes = _client.GetStatusCodes(court);

                            Log.Information("Statute reporting complete");
                            */

                            //
                            // IdentificationID node within SendingMDELocationID in the XML will be populated 
                            // with the ELS SOAP endpoint URL where the accept/reject response provided in 
                            // step 5.2 “OFS Response Sent to ELS SOAP Endpoint” is to be sent to.
                            //

                            var callbackUrlElement = (from el in xml.Descendants()
                                                      where el.Name.Namespace == "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0"
                                                      && el.Name == string.Format("{{{0}}}{1}", el.Name.Namespace, "SendingMDELocationID")
                                                      select el.Element(string.Format("{{{0}}}{1}", "http://niem.gov/niem/niem-core/2.0", "IdentificationID"))).FirstOrDefault();
                            callbackUrlElement.Value = @ConfigurationManager.AppSettings.Get("notifyReviewCallbackUrl") ?? "";
                            Log.Information("Callback filing notification URL = {0}", callbackUrlElement.Value);


                            var entityPerson = xml.Descendants(@"{urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0}EntityPerson");
                            var element = entityPerson?.Where(attr => attr.Attribute("{http://niem.gov/niem/structures/2.0}id")?.Value == "DEF");
                            var personName = element?.FirstOrDefault()?.Element("{http://niem.gov/niem/niem-core/2.0}PersonName");
                            var firstName = personName?.Element("{http://niem.gov/niem/niem-core/2.0}PersonGivenName")?.Value ?? "";
                            var middleName = personName?.Element("{http://niem.gov/niem/niem-core/2.0}PersonMiddleName")?.Value ?? "";
                            var lastName = personName?.Element("{http://niem.gov/niem/niem-core/2.0}PersonSurName")?.Value ?? "";
                            //defendantFullName = String.Format("{0}, {1} {2}", lastName, firstName, middleName) ?? "";
                            defendantFullName = string.IsNullOrEmpty(defendantFullName) ? $"{lastName}, {firstName} {middleName}" : defendantFullName;
                            Log.Information("765: defendantFullName {0}", defendantFullName);
                            

                            // Send review filing to Odyssey API system
                            Log.Information("Sending review file to EFMClient");
                            var feesCalculation = _client.GetFeesCalculation(xml, userResponse);
                            Log.Information("feesCalculation {0}", feesCalculation);
                            Log.Information("xml: {0}", xml.ToString());
                            XElement ofsResult;
                            if (authFilingUserResponse != null && !string.IsNullOrEmpty(authFilingUserResponse.Email) && !string.IsNullOrEmpty(authFilingUserResponse.UserID))
                            {
                                Log.Information($"Authenticated User Filing Email: {authFilingUserResponse.Email}");
                                authenticatedFilingUser += $"{authFilingUserResponse.FirstName} { authFilingUserResponse.LastName}";
                                ofsResult = _client.ReviewFiling(xml, authFilingUserResponse);
                            }
                            else if (authDefaultFilingUserResponse != null && !string.IsNullOrEmpty(authDefaultFilingUserResponse.Email) && !string.IsNullOrEmpty(authDefaultFilingUserResponse.UserID))
                            {
                                Log.Information($"Authenticated User Filing Email: {authDefaultFilingUserResponse.Email}");
                                authenticatedFilingUser += $"{authDefaultFilingUserResponse.Email}";
                                ofsResult = _client.ReviewFiling(xml, authDefaultFilingUserResponse);
                            }
                            else
                            {
                                throw new Exception($"The filer {authFilingUserResponse.Email} and default filer {authDefaultFilingUserResponse.Email} could not be authenticated.");
                                /*
                                 * authenticatedFilingUser += $"{userResponse.FirstName} {userResponse.LastName}";
                                Log.Information($"Authenticated User Filing Email: {userResponse.Email}");
                                ofsResult = _client.ReviewFiling(xml, userResponse);
                                */
                            }
                            Log.Information("Review filing complete : {0}", ofsResult);
                            //Log.Information("Filed Documents {0}", xml.Descendants().Where(x => x.Name.LocalName == "DocumentFileControlID"));

                            var filingObjects = ofsResult.Elements("{http://niem.gov/niem/niem-core/2.0}DocumentIdentification")?.Where(x => (string)x?.Element("{http://niem.gov/niem/niem-core/2.0}IdentificationCategoryText") == "FILINGID");
                            //Log.Information("filingObjects {0}", filingObjects?.Select(x => (string)x?.Element("{http://niem.gov/niem/niem-core/2.0}IdentificationID"))?.ToList());
                            var filingGUIDS = filingObjects?.Select(x => (string)x?.Element("{http://niem.gov/niem/niem-core/2.0}IdentificationID"))?.ToList();
                            Log.Information("filingGUIDS {0}", filingGUIDS);
                            String fromDateQuery = DateTime.Today.ToString(); //string.IsNullOrEmpty(fromDateParam) ? "2022-04-01" : fromDateParam;
                            String toDateQuery = DateTime.Now.ToString(); //string.IsNullOrEmpty(toDateParam) ? "2022-04-26" : toDateParam;
                            /*
                            //test
                            var filingGUIDtest = "ed8f2505-e25e-4a60-be09-8d35d7ef8189";
                            var getFilingDetailsResponsetest = _client.GetFilingDetails(userResponse, "fresno:cr", filingGUIDtest);
                            var getFilingStatusRestponsetest = _client.GetFilingStatus(userResponse, "fresno:cr", filingGUIDtest);
                            Log.Information("filingGUID {0} getFilingDetailsResponse {1}", filingGUIDtest, getFilingDetailsResponsetest);
                            Log.Information("filingGUID {0} getFilingStatusRestponse {1}", filingGUIDtest, getFilingStatusRestponsetest);
                            */
                            foreach (var filingGUID in filingGUIDS)
                            {
                                XNamespace ncNamespace = "http://niem.gov/niem/niem-core/2.0";
                                var getFilingListResponse = _client.GetFilingList(userResponse, courtLocation, firmUserID, fromDateQuery, toDateQuery);
                                Log.Information("GetFilingList params: court:{0} firmUserId:{1} fromDate:{2} toDate:{3}", courtLocation, firmUserID, fromDateQuery, toDateQuery);
                                var getFilingDetailsResponse = _client.GetFilingDetails(userResponse, courtLocation, filingGUID);
                                var getFilingStatusRestponse = _client.GetFilingStatus(userResponse, courtLocation, filingGUID);
                                Log.Information("filingGUID {0} getFilingDetailsResponse {1}", filingGUID, getFilingDetailsResponse);
                                Log.Information("filingGUID {0} getFilingStatusRestponse {1}", filingGUID, getFilingStatusRestponse);
                                Log.Information("caseTitleText {0}", caseTitleText);
                                var responseFilingLeadDocument = getFilingDetailsResponse.Descendants().Where(x => x.Name.LocalName == "FilingLeadDocument").FirstOrDefault();
                                Log.Information("responseFilingLeadDocument {0}", responseFilingLeadDocument);
                                var documentSequent = responseFilingLeadDocument.Descendants().Where(x => x.Name.LocalName == "DocumentSequenceID" && x.Value == "0").FirstOrDefault();
                                Log.Information("documentSequence {0}", documentSequent);
                                var documentIdentificationList = getFilingDetailsResponse.Descendants().Where(x => x.Name.LocalName == "DocumentIdentification");
                                Log.Information("documentIdentificationList {0}", documentIdentificationList);
                                foreach (var documentIdentification in documentIdentificationList)
                                {
                                    Log.Information("748:documentSequent {0}", documentSequent);
                                    //Log.Information($"{documentIdentification}");
                                    if (documentSequent != null && documentSequent?.Value?.ToString() == "0" && documentIdentification?.Element(ncNamespace + "IdentificationCategoryText")?.Value == "FILINGID")
                                    {
                                        Log.Information("751:documentIdentification: {0}", documentIdentification);
                                        if (!string.IsNullOrEmpty(documentIdentification?.Element(ncNamespace + "IdentificationID")?.Value)) {
                                            filingDocumentsGUIDLeadDoc = documentIdentification?.Element(ncNamespace + "IdentificationID")?.Value ?? "";
                                            Log.Information("755:filingDocumentsGUIDLeadDoc: {0}", filingDocumentsGUIDLeadDoc);
                                        }
                                    }
                                }
                                
                                if (string.IsNullOrEmpty(caseTitleText))
                                {
                                    var testCaseTitleText = getFilingStatusRestponse.Descendants().Where(x => x.Name.LocalName == "CaseTitleText")?.FirstOrDefault().Value ?? "";
                                    Log.Information("{0}", getFilingStatusRestponse.Descendants().Where(x => x.Name.LocalName == "CaseTitleText"));
                                    Log.Information("testCaseTitleText {0}", testCaseTitleText);
                                    caseTitleText = testCaseTitleText;
                                }
                                Log.Information("caseTitleText is null or empty? {0} {1}", string.IsNullOrEmpty(caseTitleText), caseTitleText);
                            }

                            foreach (var filedDocument in xml.Descendants().Where(x => x.Name.LocalName == "DocumentFileControlID"))
                            {
                                Log.Information("Doc {0}", filedDocument.Value);
                                filedDocuments.Add(filedDocument.Value);
                            }
                            // Report ofsResults and send response back to ePros interface
                            Log.Information("Complete, logging ofsResults");
                            XNamespace ecfNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
                            var errors = ofsResult.Descendants(ecfNamespace + "Error").ToList();
                            if (errors != null && errors.Count() > 0) // filing errors?
                            {
                                // Search for successful results (Errorcode=="0" && ErrorText=="No Error")
                                var resultSuccess = (from el in ofsResult.Descendants(ecfNamespace + "Error")
                                                     where
                                                        el.Element(ecfNamespace + "ErrorCode").Value == "0"
                                                        && el.Element(ecfNamespace + "ErrorText").Value == "No Error"
                                                     select el
                                                    )?.FirstOrDefault();

                                // Dump error log results
                                Log.Information("EFM reviewFiling response errors:");
                                for (int i = 0; i < errors.Count(); i++)
                                    Log.Information(string.Format("{0}-{1}", i, errors[i].ToString()));

                                if (resultSuccess != null)
                                {
                                    Log.Information("Ofs Review Filing was successful, no errors reported");
                                    MoveFile(file, string.Format(@"{0}\{1}", _filingSuccessPath, fileName));
                                } else
                                {
                                    MoveFile(file, string.Format(@"{0}\{1}", _filingFailedPath, fileName));
                                }

                                XElement messageReceiptMessageCaseId = ofsResult.Descendants().Where(x => x.Name.LocalName.Equals("IdentificationCategoryText") && x.Value.Equals("CASEID"))?.FirstOrDefault().Parent;
                                Log.Information($"messageReceiptMessageCaseId:{messageReceiptMessageCaseId} caseListCount:{caseList.Count()}");
                                foreach (var message in messageReceiptMessageCaseId.Descendants().Where(x => x.Name.LocalName.Equals("IdentificationID") && x.Value != null))
                                {
                                    CourtCase courtCase = new CourtCase();
                                    courtCase.caseTrackingID = message.Value ?? "";
                                    Log.Information($"message:{message.Value}");
                                    caseList.Add(courtCase);
                                }
                                // Send ePros response information, if response fails keep file in queue
                                if (SendEProsResponseMessage(authenticatedFilingUser, courtLocation, eProsCfg?.filingDocID, ofsResult, caseTitleText, filingDocumentsGUIDLeadDoc, filedDocuments, defendantFullName, caseList, "")) // valid response?
                                {
                                    Log.Information("SendEProsResponseMessage:2");
                                    /*if (resultSuccess != null) // success, move to 'success' folder
                                        MoveFile(file, string.Format(@"{0}\{1}", _filingSuccessPath, fileName));
                                    else  // failed, move to 'failed' folder
                                        MoveFile(file, string.Format(@"{0}\{1}", _filingFailedPath, fileName));*/

                                    // Write submit filing response message to disk
                                    Log.Information("SendEProsResponseMessage successful ofsResult {0}", ofsResult);
                                    WriteSubmitFilingResponse(eProsCfg?.filingDocID, ofsResult);
                                }
                                else
                                {
                                    Log.Information("ePros REST response send failed, review callback endpoint.");
                                }
                            }
                            else
                            {
                                Log.Information("SendEProsResponseMessage:3");
                                // Send response w/ exception back to ePros indicating invalid Ofs response message
                                SendEProsResponseMessage(authenticatedFilingUser, courtLocation, eProsCfg?.filingDocID, ofsResult, caseTitleText, filingDocumentsGUIDLeadDoc, filedDocuments, defendantFullName, caseList, "Invalid Ofs MessageReceipt xml, no <Error> section found");
                                MoveFile(file, string.Format(@"{0}\{1}", _filingFailedPath, fileName));

                                // Write submit filing response message to disk
                                WriteSubmitFilingResponse(eProsCfg?.filingDocID, ofsResult);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Information($"SendEProsResponseMessage:4; caseListCount:{caseList.Count()}");
                            Log.Fatal(ex, "Exception::CheckForOutboundMessages - review file processing error");
                            
                            // Send ePros exception fault reponse message
                            SendEProsResponseMessage(authenticatedFilingUser, courtLocation, eProsCfg?.filingDocID, null, caseTitleText, filingDocumentsGUIDLeadDoc, filedDocuments, defendantFullName, caseList, ex.Message);
                                
                            // On failure, move to 'failed' folder
                            MoveFile(file, string.Format(@"{0}\{1}", _filingFailedPath, fileName));
                        }
                    }
                }
                else
                {
                    Log.Information("No ofs reviewFilings found to process");
                }
            } catch ( Exception ex ) {
                Log.Fatal(ex, "Exception::CheckForOutboundMessages - Error processing outbound messages");
            }
        }

        // 
        // Send response message to ePros interface
        // @param sSubDocRefID = Submitting ePros Document Id
        // @param xOfsResponse = Response from reviewFiling submission to report to ePros
        // @param sExceptionFault = Exception response message
        // @return true if successful, else false if error
        //
        private Boolean SendEProsResponseMessage(String authenticatedFilingUser, String filingLocation, String sSubDocRefID, XElement xOfsResponse, String caseTitleText, String filingDocumentsGUIDLeadDoc, List<String> filedDocuments, String defendantFullName, List<CourtCase> caseListQuery, String sExceptionFault = "" ) {

            try
            {
                // Rule Code format to follow for ePros REST services
                /*{
                      "ruleCode": "Interface_OFS_ProcessReviewFilingResponseMessages",
                      "inputParams": {
                        "params": [
                          {
                            "name": "rfResponseJson",
                            "value": {
                              "ePros": {
                                "submitDocRefId": 400
                              },
                              "reviewFilingResponse": true,
                              "caseDocketId": "CV-01194-2015",
                              "caseTrackingId": "",
                              "caseFilingId": "",
                              "caseFilingDate": "",
                              "organizationId": "fresno:cr",
                              "statusErrorList": [
                                {
                                  "statusCode": "0",
                                  "statusText": ""
                                }
                              ],
                              "exception": ""  
                            }
                          }
                        ]
                      }
                    }
                */
                
                FilingResponseObj responseObj = null;

                // Create XML namespaces for xml linq search
                XNamespace ncNamespace = "http://niem.gov/niem/niem-core/2.0";
                XNamespace ecfNamespace = "urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0";
                XNamespace jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";

                // Load all response xml fields into responseLog
                if (String.IsNullOrEmpty(sExceptionFault) && xOfsResponse != null)     // non-exception?
                {
                    Log.Information("xOfsResponse: {0}", xOfsResponse);
                    responseObj = (from el in xOfsResponse.DescendantsAndSelf()  // use DescendantsAndSelf since xml doesn't have a root node
                                   select new FilingResponseObj
                                   {
                                       // Search for caseFilingID if DocumentIdentification elements > 1 then "FILINGID" will follow else if single DocumentIdentification no "FILINGID",
                                       // so handle both conditional types.
                                       /*caseFilingId = el?.Elements(ncNamespace + "DocumentIdentification")?.Where(x => (string)x?.Element(ncNamespace + "IdentificationCategoryText") == "FILINGID")?.
                                                                   Select(x => (string)x?.Element(ncNamespace + "IdentificationID"))?.FirstOrDefault() ??
                                                                   el?.Element(ncNamespace + "DocumentIdentification")?.Element(ncNamespace + "IdentificationID")?.Value ?? "",*/
                                       caseFilingId = filingDocumentsGUIDLeadDoc,
                                       filingEnvelopeId = el?.Elements(ncNamespace + "DocumentIdentification")?.Where(x => (string)x?.Element(ncNamespace + "IdentificationCategoryText") == "ENVELOPEID")?.Select(x => (string)x?.Element(ncNamespace + "IdentificationID"))?.FirstOrDefault() ?? "",
                                       filingCaseTitleText = caseTitleText,
                                       authenticatedFilingUser = authenticatedFilingUser,
                                       filingDocuments = filedDocuments,
                                       filingDocumentsGUID = (List<String>)el?.Elements(ncNamespace + "DocumentIdentification")?.Where(x => (string)x?.Element(ncNamespace + "IdentificationCategoryText") == "FILINGID")?.Select(x => (string)x?.Element(ncNamespace + "IdentificationID"))?.ToList(),
                                       filingDefendantFullName = defendantFullName,
                                       caseListResponse = caseListQuery,
                                       // Load response error list
                                       statusErrorList = el?.Elements(ecfNamespace + "Error")
                                           .Select(er => new FilingRespError
                                           {
                                               statusCode = er?.Elements()?.Where(e => (string)e?.Name?.LocalName?.ToLower() == "errorcode")?.First()?.Value ?? "",
                                               statusText = er?.Elements()?.Where(e => (string)e?.Name?.LocalName?.ToLower() == "errortext")?.First()?.Value ?? ""
                                           })?.ToList(),
                                   })?.FirstOrDefault();
                    //Log.Information($"TEST: responseObj.caseListResponse:{responseObj.caseListResponse}");
                    if ( responseObj != null ) // invalid object?
                        {
                            // The subDocRefID is the ePros document.id used by response interface as a ref to the submitting case/doc mainly for first time filing
                            responseObj.ePros.submitDocRefId = sSubDocRefID ?? "";
                            responseObj.reviewFilingResponse = true;    // indicate sync response message
                            responseObj.caseListResponse = caseListQuery;
                            responseObj.organizationId = filingLocation;

                        // Remove any '\' characters in the error description
                        if ( responseObj.statusErrorList != null )
                            {
                                foreach (var error in responseObj.statusErrorList)
                                {
                                    Regex regex = new Regex(@"[\\]+");
                                    error.statusText = regex.Replace(error.statusText, "");
                                }
                            }
                    }
                    else {
                            sExceptionFault = "Exception:SendEProsResponseMessage - Service error processing ofs submit response filing json";
                        }
                }

                // Process/Send exception response message to ePros if needed
                if( responseObj == null && !String.IsNullOrEmpty(sExceptionFault) )  // exception?
                {
                    responseObj = new FilingResponseObj();
                    responseObj.ePros.submitDocRefId = sSubDocRefID ?? "";
                    responseObj.organizationId = filingLocation;
                    responseObj.exception = sExceptionFault ?? "Unknown exception fault";
                    responseObj.caseListResponse = caseListQuery;

                    // Remove any '\' characters in the exception message
                    Regex regex = new Regex(@"[\\]+");
                    responseObj.exception = regex.Replace(responseObj.exception, "");
                }

                // Create response Json message for ePros Rule value
                var eProsRespJson = new
                {
                    // Add root node 'rfResponse' to json message
                    rfResponse = new Newtonsoft.Json.Linq.JRaw(Newtonsoft.Json.JsonConvert.SerializeObject(responseObj, Newtonsoft.Json.Formatting.None))
                };

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
                

                var token = eRest.GetUserAuthToken(@ConfigurationManager.AppSettings.Get("eSuiteRestAPI_login"),
                                                   @ConfigurationManager.AppSettings.Get("eSuiteRestAPI_pwd"));
                var healthStatus = new eRestRequest(@ConfigurationManager.AppSettings.Get("eSuiteRestAPI_status"), token, eRestRequestTypes.Get);
                if (@ConfigurationManager.AppSettings.Get("checkHealthStatus") == "true")
                {
                    Log.Information("Health Status: {0}", eRest.SubmitRequest(healthStatus));
                }
                Log.Information("Sending response to ePros Rest Services");
                Log.Information(eProsjson);
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
                    if (cIntResp.eResponse.code != "200")
                        Log.Error(cIntResp.ToString());
                    else
                        Log.Information(cIntResp.ToString());
                }

                return true; // indicate success
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Exception::SendEProsResponseMessage - Error processing ePros filing response {0} {1}", ex.Message, ex.InnerException);
            }

            return false;
        }

        // 
        // Write submit filing response to disk using file path specified
        // Returns - nothing
        // 
        // 
        private void WriteSubmitFilingResponse(String sSubDocRefID, XElement xOfsResponse) {
            try
            {
                // Save xml notify message to network, if path is missing it's disabled
                if (xOfsResponse != null) // valid response?
                {
                    var data = XDocument.Parse(xOfsResponse.ToString());
                    var path = @ConfigurationManager.AppSettings.Get("submitReviewFolder");
                    if (!String.IsNullOrEmpty(path)) // not disabled?
                    {
                        Log.Information("Recording submit filing response message");
                        String messageId = "Submit";
                        if (!String.IsNullOrEmpty(sSubDocRefID)) // valid docketId?
                            messageId = String.Format(@"{0}-Submit", sSubDocRefID);
                        WriteFile(path, messageId, data.ToString(), true);
                    }
                }
                else
                    Log.Information("No valid OfsReponse message, submit review filing response not saved");

            }
                catch (Exception ex)
            {
                Log.Fatal(ex, "Exception::WriteSubmitResponse - Error writing submit filing repsonse");
            }
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

                Log.Information(string.Format(@"Writing {0} file", fileName));

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

        //
        // Sets up 'xsi:nil' on any elements that need it. We are controlling which elements need it
        // via the _elements variable declared above.
        // 
        // <param name="xml"></param>
        // 
        private XElement ValidateXml(XElement xml)
        {
            // Element list that must have 'xsi:nil' in the filing namespace for blank elements.    
            XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
            List<string> elementNames = new List<string>(new string[] {
                "{http://niem.gov/niem/niem-core/2.0}PersonCitizenshipFIPS10-4Code",
                "{http://niem.gov/niem/niem-core/2.0}LanguageCode",
                "{http://niem.gov/niem/domains/jxdm/4.0}DrivingJurisdictionAuthorityNCICLSTACode",
                "{http://niem.gov/niem/niem-core/2.0}LocationCountryFIPS10-4Code",
                "{http://niem.gov/niem/niem-core/2.0}DateTime"
                });

            var toBeUpdated = xml.Descendants().Where(x => elementNames.Contains(x.Name.ToString())
                && x.Attribute(xsi + "nil") == null
                && string.IsNullOrWhiteSpace(x.Value));

            foreach (var element in toBeUpdated)
            {
                element.Add(new XAttribute(xsi + "nil", true));
            }

            return xml;
        }

        //
        // Method to move file and overwrite if existing file found
        //
        private void MoveFile( String sSrcPath, String sDestPath )
        {
            Log.Information(string.Format("Moving {0} file to {1}", sSrcPath, sDestPath));
            try
            {
                if (File.Exists(sDestPath)) // file exists?
                    File.Delete(sDestPath);
                File.Move(sSrcPath, sDestPath);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, string.Format("Exception::MoveFile - Error moving file {0}", sDestPath));
            }
        }

        //
        // Service Host fault handler
        //
        private void Host_Faulted(object sender, EventArgs e)
        {
            Log.Fatal("Exception::CheckForOutboundMessages - ServiceHost faulted");
            _cHost.Abort();
        }

        //
        // Host service onStop handler
        //
        protected override void OnStop()
        {
            if (_cHost != null )
                _cHost.Close();
        }

        //
        // Retrieve all settings from app.config, and make sure the directories that we need already exist.
        // Set up polling for processing outbound files
        //
        private void RunSetup()
        {
            try
            {
                // Get OFS folder path settings
                _filingQueuePath = @ConfigurationManager.AppSettings.Get("filingQueueFolder");
                _filingFailedPath = @ConfigurationManager.AppSettings.Get("filingFailedFolder");
                _filingSuccessPath = @ConfigurationManager.AppSettings.Get("filingSuccessFolder");
                //_filingStatutePath = @ConfigurationManager.AppSettings.Get("filingStatuteFolder");
                _zipFolder = @ConfigurationManager.AppSettings.Get("zipFile");
                _codeFolder = @ConfigurationManager.AppSettings.Get("codeFolder");
                _courtLocations = new List<string>(@ConfigurationManager.AppSettings.Get("courtLocations").Split(new char[] { ';' }));
                _courtID = @ConfigurationManager.AppSettings.Get("courtID");
                _hourToCheckCodes = @ConfigurationManager.AppSettings.Get("hourToCheckCodes");
                _minutesFrom = @ConfigurationManager.AppSettings.Get("minutesFrom");
                _minutesTo = @ConfigurationManager.AppSettings.Get("minutesTo");
                // Create OFS directory paths if needed
                Log.Information("creating directories");
                Log.Information("creating queue");
                Directory.CreateDirectory(_filingQueuePath);
                Log.Information("creating failed");
                Directory.CreateDirectory(_filingFailedPath);
                Log.Information("creating success");
                Directory.CreateDirectory(_filingSuccessPath);
                //Directory.CreateDirectory(_filingStatutePath);
                Log.Information("Court Locations: {0}", _courtLocations.Count);
                Log.Information("creating zipFolder at: " + _zipFolder);
                Directory.CreateDirectory(_zipFolder);
                Log.Information("creating codeFolder at: "+ _codeFolder);
                _pfxFilePath = ConfigurationManager.AppSettings.Get("pfxFilePath");
                _privateKeyPassword = ConfigurationManager.AppSettings.Get("privateKeyPassword");
                _client = new EFMClient(_pfxFilePath, _privateKeyPassword);
                if (Directory.Exists(_codeFolder) == false)
                {
                    Directory.CreateDirectory(_codeFolder);
                }
                /*foreach (string courtLocation in _courtLocations)
                {
                    var courtId = courtLocation.Replace(System.Environment.NewLine, "").Trim();
                    var location = @"\" + courtLocation.Replace(":", "").Replace(System.Environment.NewLine, "").Trim();
                    string path = _codeFolder.Trim() + location;
                    Log.Information("Court Location Path: {0}", path);
                    if (Directory.Exists(path) == false)
                    {
                        Log.Information("Creating Court Location Directory");
                        Directory.CreateDirectory(path);
                    }
                    if (Directory.Exists(path) == true)
                    {
                        _client.GetTylerCodes(courtId);
                        Log.Information("Test: _courtID: {0}; courtLocation: {1}", _courtID, courtId);
                    }
                }*/
               /* // Get EFMClient certificate info
                _pfxFilePath = ConfigurationManager.AppSettings.Get("pfxFilePath");
                _privateKeyPassword = ConfigurationManager.AppSettings.Get("privateKeyPassword");
                _client = new EFMClient(_pfxFilePath, _privateKeyPassword);
               */
                /*if (System.DateTime.Now.Hour == int.Parse(_hourToCheckCodes) && System.DateTime.Now.Minute >= int.Parse(_minutesFrom) && System.DateTime.Now.Minute <= int.Parse(_minutesTo))
                {
                    _client.GetTylerCodes(_courtID);
                }*/
                //Log.Information("CourtID: {0}", _courtID);
                //_client.GetTylerCodes(_courtID);
                // Get the polling interval...defaults to 10 minutes
                var pollingInterval = int.Parse(ConfigurationManager.AppSettings.Get("pollingIntervalMinutes") ?? _defaultPollingInterval);
                _timer = new System.Timers.Timer
                {
                    Interval = pollingInterval * 60 * 1000    // convert to milliseconds
                };

                _timer.Elapsed += _timer_Elapsed; ;
                _timer.Enabled = true;

                // Check for messages if console execution, else wait for timer interval if deployed
                if (Environment.UserInteractive) // console mode?
                    CheckForOutboundMessages();

            } catch ( Exception ex) { 
                Log.Fatal(ex, "Exception::RunSetup - Setup parameter error");
            }

        }

        //
        // Cleanup files w/ file-creation date > than day(s) limit and remove any empty folders.
        // @param - sPath: file path to clean
        // @param - iDayDelAge: days to keep files
        //
        private void cleanupFiles(String sPath, int iDayDelAge)
        {
            // Test for valid path to cleanup
            try
            {
                if ( !(sPath != null && Directory.Exists(sPath)) )  // invalid path?
                    return;
            }
            catch (Exception) // fail siliently
            {
            }

            // Cleanup any outdated files
            try
            {
                if (iDayDelAge != 0) // enabled?
                {
                    Log.Information(String.Format("Checking {0} for files older than {1} day(s)", sPath, iDayDelAge));

                    // Check for aged files to remove
                    foreach (String file in Directory.GetFiles(sPath))
                    {
                        FileInfo fi = new FileInfo(file);
                        if (fi.LastWriteTime < DateTime.Now.AddDays(iDayDelAge * -1))  // overdue?
                        {
                            Log.Information(String.Format("Removing file {0}", fi.FullName));
                            fi.Delete();
                        }
                    }

                    // Recursively search next subfolder if available
                    foreach (String subfolder in Directory.GetDirectories(sPath))
                    {
                        cleanupFiles(subfolder, iDayDelAge);
                    }

                    // Remove empty folder
                    /*if (Directory.GetFiles(sPath).Length == 0 && Directory.GetDirectories(sPath).Length == 0)
                    {
                        Log.Information(String.Format("Removing empty folder {0}", sPath));
                        Directory.Delete(sPath);
                    }*/
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(String.Format("Exception::cleanupArchiveFiles - '{0}'", ex.Message));
            }
        }

        //
        // Event handler for timed tasks
        //
        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Archive message file management
            int iDayDelAge = Convert.ToInt32(ConfigurationManager.AppSettings["numDaysToKeepMsgFiles"] ?? "0");
            cleanupFiles(@ConfigurationManager.AppSettings["filingSuccessFolder"], iDayDelAge);
            cleanupFiles(@ConfigurationManager.AppSettings["filingFailedFolder"], iDayDelAge);
            cleanupFiles(@ConfigurationManager.AppSettings["submitReviewFolder"], iDayDelAge);
            cleanupFiles(@ConfigurationManager.AppSettings["notifyReviewFolder"], iDayDelAge);

            // Log message file management
            iDayDelAge = Convert.ToInt32(ConfigurationManager.AppSettings["numDaysToKeepLogFiles"] ?? "0");
            cleanupFiles(Path.GetDirectoryName(@ConfigurationManager.AppSettings["ofsLogFile"]), iDayDelAge);
            Log.Information("Time: Hour: {0} Minute:{1}", System.DateTime.Now.Hour, System.DateTime.Now.Minute);
            if (System.DateTime.Now.Hour == int.Parse(_hourToCheckCodes) && System.DateTime.Now.Minute >= int.Parse(_minutesFrom) && System.DateTime.Now.Minute <= int.Parse(_minutesTo))
            {
                //Get new Tyler codes
                _client.GetTylerLocationCodes();
                string locationsText = System.IO.File.ReadAllText(string.Concat(ConfigurationManager.AppSettings.Get("CodeFolder")) + @"\locations.xml");
                var xmlData = XElement.Parse(locationsText);
                //Log.Information("xmlData {0}", xmlData);
                var xmlLocationRows = xmlData.Descendants().Where(x => x.Name.LocalName == "Row" );

                //Log.Information("ROW {0}", xmlLocationRows);
                List<string> allCourtLocations = new List<string>();
                foreach( var xmlRow in xmlLocationRows)
                {
                    var simpleValue = xmlRow.Elements().Where(attr => attr.Attribute("ColumnRef")?.Value == "code")?.FirstOrDefault()?.Element("SimpleValue")?.Value ?? "";
                    Log.Information("simpleValue {0}", simpleValue);
                    if (!string.IsNullOrEmpty(simpleValue))
                    {
                        allCourtLocations.Add(simpleValue);
                    }

                }

                //foreach (string courtLocation in allCourtLocations)
                foreach (string courtLocation in _courtLocations)
                {
                    var courtId = courtLocation.Replace(System.Environment.NewLine, "").Trim();
                    var location = @"\" + courtLocation.Replace(":", "").Replace(System.Environment.NewLine, "").Trim();
                    string path = _codeFolder.Trim() + location;
                    Log.Information("Court Location Path: {0}", path);
                    if (Directory.Exists(path) == false)
                    {
                        Log.Information("Creating Court Location Directory");
                        Directory.CreateDirectory(path);
                    }
                    if (Directory.Exists(path) == true)
                    {
                        Log.Information("Downloading Tyler Court Codes for: courtLocation: {0}", courtId);
                        _client.GetTylerCodes(courtId);
                        Log.Information("Downloading Tyler Court Codes for: courtLocation: {0} complete", courtId);
                    }
                }
            }

            // Test for odyssey filings to process
            CheckForOutboundMessages();
        }

        //
        // Manually start host service w/ console support.
        //
        internal void TestStartupAndStop(string[] args)
        {
            this.OnStart(args);
            Console.ReadLine();
            this.OnStop();
        }
    }

    // 
    // Filing Response Class 
    //
    public class FilingResponseObj
    {
        public FilingRespEPros ePros { get; set; }
        public String authenticatedFilingUser { get; set; }
        public Boolean reviewFilingResponse { get; set; }
        public String caseDocketId { get; set; }
        public String caseTrackingId { get; set; }
        public String publicServiceContacts { get; set; }
        public String caseFilingId { get; set; }
        public String caseFilingIdText { get; set; }
        public String caseFilingDate { get; set; }
        public String organizationId { get; set; }
        public String filingStatusText { get; set; }
        public String filingStatusCode { get; set; }
        public String filingCaseTitleText { get; set; }
        public String filingEnvelopeId { get; set; }
        public String filingDefendantFullName { get; set; }
        public List<String> filingDocuments { get; set; }
        public List<String> filingDocumentsGUID { get; set; }
        public List<FilingRespError> statusErrorList { get; set; }
        public List<CourtCase> caseListResponse { get; set; }
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
            filingDefendantFullName = "";
            exception = "";
            caseListResponse = new List<CourtCase>();
            filingDocuments = new List<String>();
            filingDocumentsGUID = new List<String>();
            statusErrorList = new List<FilingRespError>();
            authenticatedFilingUser = "";
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

    public class CourtCase
    {
        public String caseTitleText { get; set; }
        public String caseTrackingID { get; set; }
        public String caseDocketID { get; set; }
    }

}
