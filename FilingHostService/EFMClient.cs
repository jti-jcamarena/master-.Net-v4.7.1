using FilingHostService.EFMUserService;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Xml.Linq;
using System.IO.Compression;
using System.Collections.Generic;
using Serilog;

namespace FilingHostService
{
    public class EFMClient
    {
        #region Private Properties

        private X509Certificate2 MessageSigningCertificate { get; set; }

        #endregion

        #region Constructors

        public EFMClient(X509Certificate2 certificate)
        {
            this.MessageSigningCertificate = certificate;

            // Uncomment this line to ignore server certificate errors
            // This is useful if running through a proxy (like Fiddler) to capture the message content
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        }

        public EFMClient(string pfxFilePath, string privateKeyPassword)
          : this(LoadCertificateFromFile(pfxFilePath, privateKeyPassword))
        {
        }

        public EFMClient(string subjectName)
          : this(LoadCertificateFromStore(subjectName))
        {
        }

        #endregion

        #region EFM Web Service Calls

        public AuthenticateResponseType AuthenticateUser(AuthenticateRequestType request)
        {
            EfmUserServiceClient userService = this.CreateUserService();
            userService.Open();
            AuthenticateResponseType response = userService.AuthenticateUser(request);
            userService.Close();
            return response;
        }

        public EFMFirmService.AttorneyListResponseType GetAttorneys(AuthenticateResponseType user)
        {
            var firmService = this.CreateFirmService();
            using (new OperationContextScope(firmService.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);

                var response = firmService.GetAttorneyList();

                return response;
            }
        }

        //
        // GetCaseTrackingID based on caseDocketNbr and courtID
        // @returns CaseTrackID reference or null if not found
        //
        public string GetCaseTrackingID(AuthenticateResponseType user, string courtID, string caseDocketNbr)
        {
            String caseListReqXml = @"<CaseListQueryMessage xmlns='urn: oasis: names: tc: legalxml - courtfiling:schema: xsd: CaseListQueryMessage - 4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-CaseListQueryMessage.xsd'>
	            <ecf:SendingMDELocationID>
		            <nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	            </ecf:SendingMDELocationID>
	            <ecf:SendingMDEProfileCode>
		            urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0
	            </ecf:SendingMDEProfileCode>
	            <ecf:QuerySubmitter>
		            <ecf:EntityPerson/>
	            </ecf:QuerySubmitter>
	            <j:CaseCourt>
		            <nc:OrganizationIdentification>
			            <nc:IdentificationID></nc:IdentificationID>
		            </nc:OrganizationIdentification>
	            </j:CaseCourt>
	            <CaseListQueryCase>
		            <nc:CaseTitleText/>
		            <nc:CaseCategoryText/>
		            <nc:CaseTrackingID/>
		            <nc:CaseDocketID></nc:CaseDocketID>
	            </CaseListQueryCase>
            </CaseListQueryMessage>";

            // Update xml message with the appropriate values
            XElement xml = XElement.Parse(caseListReqXml);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var caseList = xml.Elements().Where(x => x.Name.LocalName.ToLower() == "caselistquerycase")?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var caseNumberElement = caseList.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "CaseDocketID"))?.FirstOrDefault();

            // Add search criteria fields
            courtIDElement.Value = courtID;
            caseNumberElement.Value = caseDocketNbr;
                
            // Setup message header for getCaseList request
            var service = this.CreateRecordService();
            using (new OperationContextScope(service.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };

                // Execute request and parse response
                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);
                var response = service.GetCaseList(xml);
                var caseTrackingId = response.Descendants().Where(x => x.Name.LocalName.ToLower() == "casetrackingid")?.FirstOrDefault();
                return "29209abb-0519-45d2-a30d-76893d139bd1";
//                return caseTrackingId?.Value ?? null;
            }
        }

        public XElement GetPolicy(AuthenticateResponseType user)
        {
            var xml = System.IO.File.ReadAllText(@"C:\Bitlink\jobs\fresno\xml\policy_request.xml");
            var request = XElement.Parse(xml);
            var service = this.CreateFilingService();
            using (new OperationContextScope(service.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);

                var response = service.GetPolicy(request);
                return response;
            }
        }

        //
        // Request Payment Account List from EFM service
        //
        public void GetPaymentAccountList(AuthenticateResponseType user)
        {
            var service = this.CreateFirmService();
            using (new OperationContextScope(service.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);

                EFMFirmService.PaymentAccountListResponseType pmtType = service.GetPaymentAccountList();

                Log.Information("GetPaymentAccountList Results:");
                foreach( var p in pmtType.PaymentAccount)
                {
                    Log.Information(" AccountID = " + p.PaymentAccountID?.ToString());
                    Log.Information(" FirmID = " + p.FirmID?.ToString());
                    Log.Information(" PaymentAccountTypeCode = " + p.PaymentAccountTypeCode?.ToString());
                    Log.Information(" AccountName = " + p.AccountName?.ToString());
                    Log.Information(" AccountToken = " + p.AccountToken?.ToString());
                    Log.Information(" CardType = " + p.CardType?.ToString());
                    Log.Information(" CardLast4 = " + p.CardLast4?.ToString());
                    Log.Information(" CardName = " + p.CardHolderName?.ToString());
                    Log.Information(" Active = " + p.Active);
                    Log.Information("");
                }
            }
        }

        public List<StatuteCode> GetStatuteCodes(string fileName, string courtID, List<StatuteCode> statutes)
        {
            if (StatuteFileExists())
            {
                return ReadStatuteXML(fileName, statutes);
            }
            else
            {
                var zipFilePath = ConfigurationManager.AppSettings.Get("zipFile");
                var statuteFolderPath = ConfigurationManager.AppSettings.Get("CodeFolder");

                // Download and extract zip file
                var url = string.Format("https://california-stage.tylerhost.net/codeservice/codes/statute/{0}", courtID);

                var _data = Encoding.UTF8.GetBytes(DateTime.Now.ToString("o"));
                ContentInfo _info = new ContentInfo(_data);
                SignedCms _cms = new SignedCms(_info, false);
                CmsSigner _signer = new CmsSigner(this.MessageSigningCertificate);
                _cms.ComputeSignature(_signer, false);
                var _signed = _cms.Encode();
                var _b64 = Convert.ToBase64String(_signed);

                using (WebClient _client = new WebClient())
                {
                    _client.Headers["tyl-efm-api"] = _b64;
                    _client.DownloadFile(url, zipFilePath);
                }

                File.Delete(string.Format("{0}/{1}", statuteFolderPath, "statutecodes.xml"));
                ZipFile.ExtractToDirectory(zipFilePath, statuteFolderPath);

                return ReadStatuteXML(fileName, statutes);
            }
        }

        public void GetTylerCodes(string courtID)
        {         
            var zipFilePath = ConfigurationManager.AppSettings.Get("zipFile");
            var folderPath = ConfigurationManager.AppSettings.Get("CodeFolder");
            var fileNames = new List<string>(ConfigurationManager.AppSettings.Get("fileList").Split(new char[] { ';' }));
            var urls = new List<string>();
            var zips = new List<string>();
            var xmls = new List<string>();

            foreach(string fileName in fileNames)
            {
                var url = ConfigurationManager.AppSettings.Get("CourtURL").Trim('\r', '\n') + fileName.ToLower().Trim('\r', '\n') + "/" + courtID;
                url.TrimEnd('\r', '\n');
                url = String.Concat(url.Where(c => !Char.IsWhiteSpace(c)));
                urls.Add(url);
                var zipPath = zipFilePath.Trim('\r', '\n') + fileName.ToLower().Trim('\r', '\n') + ".zip";
                zipPath.TrimEnd('\r', '\n');
                zipPath = String.Concat(zipPath.Where(c => !Char.IsWhiteSpace(c)));
                zips.Add(zipPath);
                var codeFilePath = folderPath.Trim('\r', '\n') + "\\" + fileName.ToLower().Trim('\r', '\n') + "codes.xml";
                codeFilePath.TrimEnd('\r', '\n');
                codeFilePath = String.Concat(codeFilePath.Where(c => !Char.IsWhiteSpace(c)));
                xmls.Add(codeFilePath);
            }

            var _data = Encoding.UTF8.GetBytes(DateTime.Now.ToString("o"));
            ContentInfo _info = new ContentInfo(_data);
            SignedCms _cms = new SignedCms(_info, false);
            CmsSigner _signer = new CmsSigner(this.MessageSigningCertificate);
            _cms.ComputeSignature(_signer, false);
            var _signed = _cms.Encode();
            var _b64 = Convert.ToBase64String(_signed);

            using (WebClient _client = new WebClient())
            {
                _client.Headers["tyl-efm-api"] = _b64;
                for(int i = 0; i < urls.Count; i++)
                {
                    Log.Information(urls[i]);
                    Log.Information(zips[i]);
                    try
                    {
                        _client.DownloadFile(urls[i], zips[i]);
                    } catch (Exception ex)
                    {
                        Log.Error("Error: " + ex.Message + " for url " + urls[i]);
                    }
                }
            }

            for(int i = 0; i < xmls.Count; i++) {
                // Download and extract zip file               
                
                File.Delete(xmls[i]);
                ZipFile.ExtractToDirectory(zips[i], xmls[i]);
                File.Delete(zips[i]);
            }
        }

        public GetUserResponseType GetUser(GetUserRequestType request, AuthenticateResponseType user)
        {
            var userService = this.CreateUserService();
            using (new OperationContextScope(userService.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);

                var response = userService.GetUser(request);
                return response;
            }

        }

        private List<StatuteCode> ReadStatuteXML(string fileName, List<StatuteCode> statutes)
        {
            var statuteFilePath = ConfigurationManager.AppSettings.Get("statuteFile");
            var xml = XElement.Load(statuteFilePath);

            var updatedStatutes = new List<StatuteCode>();

            foreach (var statute in statutes)
            {
                //string.Format("Searching for {0}:{1}", statute.PrefixedWord, statute.Name).Dump();

                var newStatute = (from x in xml.Descendants()
                                  let row = x
                                  let codeEl = row.Elements().Where(e => e.Attribute("ColumnRef").Value == "code")
                                  let nameEl = row.Elements().Where(e => e.Attribute("ColumnRef").Value == "name")
                                  let wordEl = row.Elements().Where(e => e.Attribute("ColumnRef").Value == "word")
                                  where x.Name.LocalName.ToLower() == "row"
                                  && wordEl.Elements().First().Value.ToLower() == statute.PrefixedWord.ToLower()
                                  && nameEl.Elements().First().Value.ToLower() == statute.Name.ToLower()
                                  select new StatuteCode()
                                  {
                                      Code = decimal.Parse(codeEl.Elements().First().Value),
                                      Name = nameEl.Elements().First().Value,
                                      BaseWord = statute.BaseWord
                                  }).OrderBy(c => c.Code).FirstOrDefault();

                if (newStatute == null)
                {
                    // Couldn't find the statute
                    Log.Error(string.Format("Could not locate statute code for {0}:{1}", statute.PrefixedWord, statute.Name));
                    continue;
                }


                if (statute.AdditionalStatutes != null && statute.AdditionalStatutes.Count > 0)
                {
                    newStatute.AdditionalStatutes = new List<StatuteCode>();
                    foreach (var sub in statute.AdditionalStatutes)
                    {
                        var newSub = (from x in xml.Descendants()
                                      let row = x
                                      let codeEl = row.Elements().Where(e => e.Attribute("ColumnRef").Value == "code")
                                      let nameEl = row.Elements().Where(e => e.Attribute("ColumnRef").Value == "name")
                                      let wordEl = row.Elements().Where(e => e.Attribute("ColumnRef").Value == "word")
                                      where x.Name.LocalName.ToLower() == "row"
                                      && wordEl.Elements().First().Value == sub.PrefixedWord
                                      && nameEl.Elements().First().Value == sub.Name
                                      select new StatuteCode()
                                      {
                                          Code = decimal.Parse(codeEl.Elements().First().Value),
                                          Name = nameEl.Elements().First().Value,
                                          BaseWord = sub.BaseWord
                                      }).OrderBy(c => c.Code).FirstOrDefault();

                        if (newSub == null)
                        {
                            // Couldn't find the statute
                            Log.Error(string.Format("Could not locate statute code for {0}:{1}", sub.PrefixedWord, sub.Name));
                        }
                        else
                        {
                            newStatute.AdditionalStatutes.Add(newSub);
                        }
                    }
                }

                updatedStatutes.Add(newStatute);
            }

            return updatedStatutes;
        }

        public XElement ReviewFiling(XElement xml, AuthenticateResponseType user)
        {
            var service = this.CreateFilingService();
            using (new OperationContextScope(service.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);

                var response = service.ReviewFiling(xml);
                return response;
            }
        }

        private bool StatuteFileExists()
        {
            var statuteFilePath = ConfigurationManager.AppSettings.Get("statuteFile");
            bool fileExists = File.Exists(statuteFilePath);

            if (fileExists)
            {
                var today = DateTime.Now.Date;
                var fileDate = File.GetCreationTime(statuteFilePath);

                fileExists = fileDate >= today;
            }

            return fileExists;
        }
        #endregion

        #region Private Methods - Create EFM Web Service Client

        protected EfmUserServiceClient CreateUserService()
        {
            var client = new EfmUserServiceClient();
            client.ClientCredentials.ClientCertificate.Certificate = this.MessageSigningCertificate;
            return client;
        }

        protected EFMFilingReviewService.FilingReviewMDEServiceClient CreateFilingService()
        {
            var client = new EFMFilingReviewService.FilingReviewMDEServiceClient();
            client.ClientCredentials.ClientCertificate.Certificate = this.MessageSigningCertificate;
            return client;
        }

        protected EFMFirmService.EfmFirmServiceClient CreateFirmService()
        {
            var client = new EFMFirmService.EfmFirmServiceClient();
            client.ClientCredentials.ClientCertificate.Certificate = this.MessageSigningCertificate;
            return client;
        }

        public CourtRecordMDEService.CourtRecordMDEServiceClient CreateRecordService()
        {
            var client = new CourtRecordMDEService.CourtRecordMDEServiceClient();
            client.ClientCredentials.ClientCertificate.Certificate = this.MessageSigningCertificate;
            return client;
        }

        #endregion

        #region Private Methods - Load Certificate

        private static X509Certificate2 LoadCertificateFromStore(string subjectName)
        {
            // Open the Certificates (Local Computer) --> Personal certificate store
            X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            // Find a particular certificate by Subject Name
            X509Certificate2 certificate = store.Certificates.Find(X509FindType.FindBySubjectName, subjectName, false).OfType<X509Certificate2>().FirstOrDefault();

            // Close the certificate store
            store.Close();

            return certificate;
        }

        private static X509Certificate2 LoadCertificateFromFile(string pfxFilePath, string privateKeyPassword)
        {
            // Load the certificate from a file, specifying the password
            X509Certificate2 certificate = new X509Certificate2(pfxFilePath, privateKeyPassword);
            return certificate;
        }

        #endregion
    }
}
