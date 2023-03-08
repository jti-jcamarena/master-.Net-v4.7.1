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
            Log.Information("AuthenticateUser 1");
            EfmUserServiceClient userService = this.CreateUserService();
            Log.Information("AuthenticateUser 2");
            userService.Open();
            Log.Information("AuthenticateUser 3");
            AuthenticateResponseType response = userService.AuthenticateUser(request);
            Log.Information("AuthenticateUser 4");
            userService.Close();
            Log.Information("AuthenticateUser 5");
            return response;
        }

        public EFMFirmService.UserListResponseType GetUserList(AuthenticateResponseType user)
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
                Log.Information("79: GetUserList 1");
                var response = firmService.GetUserList();
                Log.Information("79: GetUserList 2");
                return response;
            }
        }

        public EFMFirmService.UpdateUserResponseType UpdateUser(AuthenticateResponseType authUser, EFMFirmService.UserType user, String firstName, String middleName, String lastName, String email)
        {
            var firmService = this.CreateFirmService();
            using (new OperationContextScope(firmService.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = authUser.Email,
                    Password = authUser.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);
                EFMFirmService.UpdateUserRequestType updateUserRequest = new EFMFirmService.UpdateUserRequestType();
                updateUserRequest.User = user;
                user.FirstName = firstName;
                user.MiddleName = middleName;
                user.LastName = lastName;
                user.Email = email;
                
                EFMFirmService.UpdateUserResponseType response = firmService.UpdateUser(updateUserRequest);
                
                return response;
            }
        }

        public EFMFirmService.ResetPasswordResponseType ResetUserPassword(AuthenticateResponseType authUser, string userId, string email, string password)
        {
            var firmService = this.CreateFirmService();
            using (new OperationContextScope(firmService.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = authUser.Email,
                    Password = authUser.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);
                EFMFirmService.ResetUserPasswordRequestType request = new EFMFirmService.ResetUserPasswordRequestType();
                request.UserID = userId;
                request.Email = email;
                request.Password = password;

                EFMFirmService.ResetPasswordResponseType response = firmService.ResetUserPassword(request);

                return response;
            }
        }

        public EFMFirmService.BaseResponseType AddUserRole(AuthenticateResponseType authUser, string userId, EFMFirmService.RoleType role, string location)
        {
            var firmService = this.CreateFirmService();
            using (new OperationContextScope(firmService.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = authUser.Email,
                    Password = authUser.PasswordHash
                };

                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);
                
                EFMFirmService.AddUserRoleRequestType request = new EFMFirmService.AddUserRoleRequestType();
                request.UserID = userId;
                request.Location = location;
                request.Role = role;
                EFMFirmService.BaseResponseType response = firmService.AddUserRole(request);

                return response;
            }
        }

        public System.Threading.Tasks.Task<EFMFirmService.UserListResponseType> GetUserListAsync(AuthenticateResponseType user)
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
                Log.Information("99: GetUserList 1");
                var response = firmService.GetUserListAsync();
                Log.Information("101: GetUserList 2");
                return response;
            }
        }

        public EFMFirmService.GetUserResponseType GetFirmUser(AuthenticateResponseType user)
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
                EFMFirmService.GetUserRequestType getUserRequestType = new EFMFirmService.GetUserRequestType();
                getUserRequestType.UserID = user.UserID;
                var response = firmService.GetUser(getUserRequestType);

                return response;
            }
        }

        public EFMFirmService.ServiceContactListResponseType GetPublicList(AuthenticateResponseType user, string svcEmail, string svcFirstName, string svcLastName, string svcFirmName)
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
                EFMFirmService.GetPublicListRequestType getPublicListRequestType = new EFMFirmService.GetPublicListRequestType();
                getPublicListRequestType.Email = svcEmail;
                getPublicListRequestType.FirstName = svcFirstName;
                getPublicListRequestType.LastName = svcLastName;
                getPublicListRequestType.FirmName = svcFirmName;
                var response = firmService.GetPublicList(getPublicListRequestType);
                //Log.Information("firmService.GetPublicList: response {0}", response);
                return response;
            }
        }

        public void AttachServiceContact(AuthenticateResponseType user, string caseID, string casePartyID, string svcContactID)
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
                EFMFirmService.AttachServiceContactRequestType attachServiceContactRequestType = new EFMFirmService.AttachServiceContactRequestType();
                attachServiceContactRequestType.CaseID = caseID;
                attachServiceContactRequestType.CasePartyID = casePartyID;
                attachServiceContactRequestType.ServiceContactID = svcContactID;
                var response = firmService.AttachServiceContact(attachServiceContactRequestType);
                Log.Information("AttachServiceContact {0} to {1}", svcContactID, caseID);
            }
        }

        public void DetachServiceContact(AuthenticateResponseType user, string caseID, string casePartyID, string svcContactID)
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
                EFMFirmService.DetachServiceContactRequestType detachServiceContactRequestType = new EFMFirmService.DetachServiceContactRequestType();
                detachServiceContactRequestType.CaseID = caseID;
                detachServiceContactRequestType.CasePartyID = casePartyID;
                detachServiceContactRequestType.ServiceContactID = svcContactID;
                var response = firmService.DetachServiceContact(detachServiceContactRequestType);
                Log.Information("DetachServiceContact {0} to {1}", svcContactID, caseID);
            }
        }

        public EFMFirmService.ServiceContactListResponseType GetContactList(AuthenticateResponseType user)
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

                var response = firmService.GetServiceContactList();
                Log.Information("firmService.GetServiceContactList: response {0}", response);
                return response;
            }
        }

        public EFMFirmService.GetServiceContactResponseType GetServiceContact(AuthenticateResponseType user, String contactID)
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
                EFMFirmService.GetServiceContactRequestType getServiceContactRequestType = new EFMFirmService.GetServiceContactRequestType();
                getServiceContactRequestType.ServiceContactID = contactID;
                var response = firmService.GetServiceContact(getServiceContactRequestType);

                return response;
            }
        }

        public String CreateServiceContact(AuthenticateResponseType user, string firstName, string middleName, string lastName, string phoneNumber, string email, string address1, string address2, string city, string zipCode, string state, string isPublic, string adminCopy, string firmID)
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
                EFMFirmService.CreateServiceContactRequestType createServiceContactRequestType = new EFMFirmService.CreateServiceContactRequestType();
                EFMFirmService.ServiceContactType serviceContactType = new EFMFirmService.ServiceContactType();
                
                serviceContactType.FirstName = firstName;
                serviceContactType.MiddleName = middleName;
                serviceContactType.LastName = lastName;
                serviceContactType.IsPublic = true;
                serviceContactType.IsPublicSpecified = true;
                serviceContactType.PhoneNumber = phoneNumber;
                serviceContactType.Email = email;
                serviceContactType.AdministrativeCopy = adminCopy;
                serviceContactType.FirmID = firmID;
                if (!string.IsNullOrEmpty(address1) && !string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(zipCode) && !string.IsNullOrEmpty(state)) {
                    EFMFirmService.AddressType addressType = new EFMFirmService.AddressType();
                    addressType.AddressLine1 = address1;
                    addressType.AddressLine2 = address2;
                    addressType.City = city;
                    addressType.ZipCode = zipCode;
                    addressType.State = state;
                    addressType.Country = "US";
                    serviceContactType.Address = addressType;
                }
                createServiceContactRequestType.ServiceContact = serviceContactType;
                var responseSvcContact = firmService.CreateServiceContact(createServiceContactRequestType);
                String response;
                if (responseSvcContact.Error.ErrorText == "No Error") {
                    response = responseSvcContact.ServiceContactID;
                }
                else
                {
                    response = "ErrorText: " + responseSvcContact.Error.ErrorText;
                }
                return response;
            }
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

        public String CreateAttorney(AuthenticateResponseType user, string attorneyBar, string attorneyFirstName, string attorneyMiddleName, string attorneyLastName, string firmID)
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
                FilingHostService.EFMFirmService.CreateAttorneyRequestType createAttorneyRequest = new FilingHostService.EFMFirmService.CreateAttorneyRequestType();
                EFMFirmService.AttorneyType attorney = new EFMFirmService.AttorneyType();
                attorney.BarNumber = attorneyBar;
                attorney.FirstName = attorneyFirstName;
                attorney.MiddleName = attorneyMiddleName;
                attorney.LastName = attorneyLastName;
                attorney.FirmID = firmID;
                createAttorneyRequest.Attorney = attorney;
                var response = firmService.CreateAttorney(createAttorneyRequest);
                return response.Error.ErrorCode == "0" ? $"{response.AttorneyID}" : $"errText:{response.Error.ErrorText} errCode:{response.Error.ErrorCode}";
            }
        }

        //
        // GetCaseTrackingID based on caseDocketNbr and courtID
        // @returns CaseTrackID reference or null if not found
        //
        public string GetCaseTrackingID(AuthenticateResponseType user, string courtID, string caseDocketNbr)
        {
            String caseListReqXml = @"<CaseListQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-CaseListQueryMessage.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID />
		</nc:OrganizationIdentification>
	</j:CaseCourt>
	<CaseListQueryCase>
		<nc:CaseTitleText />
		<nc:CaseCategoryText />
		<nc:CaseTrackingID />
		<nc:CaseDocketID />
	</CaseListQueryCase>
    <CaseListQueryTimeRange>
    </CaseListQueryTimeRange>
</CaseListQueryMessage>
";

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
                Log.Information("GetCaseListRequest {0}", xml);
                System.Xml.Linq.XElement response = service.GetCaseList(xml);
                Log.Information("GetCaseList Response:{0} ", response.ToString());
                var caseTrackingId = response.Descendants().Where(x => x.Name.LocalName.ToLower() == "casetrackingid")?.FirstOrDefault();
                Log.Information("case tracking id: {0}", caseTrackingId.Value);
                return caseTrackingId?.Value ?? null;
            }
        }

        public System.Xml.Linq.XElement GetCase(AuthenticateResponseType user, string courtID, string caseTrackingID, bool includeParticipantsIndicator)
        {
            String getCaseString = @"<CaseQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-CaseQueryMessage.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID/>
		</nc:OrganizationIdentification>
		<j:CourtName />
	</j:CaseCourt>
	<nc:CaseTrackingID/>
	<CaseQueryCriteria>
		<IncludeParticipantsIndicator>true</IncludeParticipantsIndicator>
		<IncludeDocketEntryIndicator>false</IncludeDocketEntryIndicator>
		<IncludeCalendarEventIndicator>false</IncludeCalendarEventIndicator>
		<DocketEntryTypeCodeFilterText>false</DocketEntryTypeCodeFilterText>
		<CalendarEventTypeCodeFilterText>false</CalendarEventTypeCodeFilterText>
	</CaseQueryCriteria>
</CaseQueryMessage>";

            XElement xml = XElement.Parse(getCaseString);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var caseTracking = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "CaseTrackingID"))?.FirstOrDefault();
            courtIDElement.Value = courtID;
            caseTracking.Value = caseTrackingID;
            var service = this.CreateRecordService();
            using (new OperationContextScope(service.InnerChannel))
            {
                var userInfo = new UserInfo()
                {
                    UserName = user.Email,
                    Password = user.PasswordHash
                };
                
                var messageHeader = MessageHeader.CreateHeader("UserNameHeader", "urn:tyler:efm:services", userInfo);
                OperationContext.Current.OutgoingMessageHeaders.Add(messageHeader);
                System.Xml.Linq.XElement response = service.GetCase(xml);
                return response;
            }
        }

        public System.Xml.Linq.XElement GetCaseList(AuthenticateResponseType user, string courtID, string caseDocketNbr)
        {
            String caseListReqXml = @"<CaseListQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-CaseListQueryMessage.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID />
		</nc:OrganizationIdentification>
	</j:CaseCourt>
	<CaseListQueryCase>
		<nc:CaseTitleText />
		<nc:CaseCategoryText />
		<nc:CaseTrackingID />
		<nc:CaseDocketID />
	</CaseListQueryCase>
    <CaseListQueryTimeRange>
    </CaseListQueryTimeRange>
</CaseListQueryMessage>
";
            Log.Information("GetCaseList 1");
            // Update xml message with the appropriate values
            XElement xml = XElement.Parse(caseListReqXml);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var caseList = xml.Elements().Where(x => x.Name.LocalName.ToLower() == "caselistquerycase")?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var caseNumberElement = caseList.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "CaseDocketID"))?.FirstOrDefault();
            Log.Information("GetCaseList 2");
            // Add search criteria fields
            courtIDElement.Value = courtID;
            caseNumberElement.Value = caseDocketNbr;
            Log.Information("GetCaseList 3");
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
                Log.Information("GetCaseListRequest {0}", xml);
                System.Xml.Linq.XElement response = service.GetCaseList(xml);
                return response;
            }
        }

        public System.Xml.Linq.XElement GetCaseListByParty(AuthenticateResponseType user, string courtID, string firstname, string lastname)
        {
            String caseListReqXml = @"<CaseListQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CaseListQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-CaseListQueryMessage.xsd'>
<ecf:SendingMDELocationID>
<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
</ecf:SendingMDELocationID>
<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
<ecf:QuerySubmitter>
<ecf:EntityPerson/>
</ecf:QuerySubmitter>
<j:CaseCourt>
<nc:OrganizationIdentification>
<nc:IdentificationID>harris:dc</nc:IdentificationID>
</nc:OrganizationIdentification>
</j:CaseCourt>
<CaseListQueryCaseParticipant>
<ecf:CaseParticipant>
<ecf:EntityPerson>
<nc:PersonName>
<nc:PersonGivenName>Jose-test</nc:PersonGivenName>
<nc:PersonMiddleName/>
<nc:PersonSurName>Camarena-test</nc:PersonSurName>
</nc:PersonName>
</ecf:EntityPerson>
<ecf:CaseParticipantRoleCode/>
</ecf:CaseParticipant>
</CaseListQueryCaseParticipant>
</CaseListQueryMessage>
";
            Log.Information("GetCaseList 1");
            // Update xml message with the appropriate values
            XElement xml = XElement.Parse(caseListReqXml);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var caseList = xml.Elements().Where(x => x.Name.LocalName.ToLower() == "caselistquerycase")?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var personName = xml.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "PersonName"));
            var personGivenName = personName.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "PersonGivenName"))?.FirstOrDefault();
            var personSurName = personName.Descendants().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "PersonSurName"))?.FirstOrDefault();
            //Log.Information("GetCaseList 2 person {0}: {1} {2}", personName, personGivenName, personSurName);
            // Add search criteria fields
            courtIDElement.Value = courtID;
            personGivenName.Value = firstname;
            personSurName.Value = lastname;
            Log.Information("xml request {0}", xml);

            
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
                Log.Information("GetCaseListRequest {0}", xml);
                System.Xml.Linq.XElement response = service.GetCaseList(xml);
                return response;
            }
        }

        public XElement GetPolicy(AuthenticateResponseType user, string courtLocation)
        {
            DateTime timestamp = DateTime.Now;
            
            //var xml = System.IO.File.ReadAllText(@"C:\Bitlink\jobs\fresno\xml\policy_request.xml");
           /* var xmlRequest = @"<policyrequest:GetPolicyRequestMessage xmlns='http://release.niem.gov/niem/niem-core/4.0/' xmlns:nc='http://release.niem.gov/niem/niem-core/4.0/' xmlns:j='http://release.niem.gov/niem/domains/jxdm/6.1/' xmlns:ecf='https://docs.oasis-open.org/legalxml-courtfiling/ns/v5.0/ecf' xmlns:policyrequest='https://docs.oasis-open.org/legalxml-courtfiling/ns/v5.0/policyrequest' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='https://docs.oasis-open.org/legalxml-courtfiling/ns/v5.0/policyrequest ../../../schema/policyrequest.xsd'>
<!--  The nc:DocumentIdentification element below is a Message Identifier (see section 6.2.5), in this circumstance, assigned by the FAMDE.  -->
<nc:DocumentIdentification>
<nc:IdentificationID>1</nc:IdentificationID>
<nc:IdentificationCategoryDescriptionText>messageID</nc:IdentificationCategoryDescriptionText>
<!--  The originating MDE that provided the message identifier  -->
<nc:IdentificationSourceText>FilingAssembly</nc:IdentificationSourceText>
</nc:DocumentIdentification>
<ecf:SendingMDELocationID>
<IdentificationID>https://eserviceprovider.com:8000</IdentificationID>
</ecf:SendingMDELocationID>
<ecf:ServiceInteractionProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-5.0</ecf:ServiceInteractionProfileCode>
<j:CaseCourt>
<nc:OrganizationIdentification>
<!--  Identifier that maps to a node  -->
<nc:IdentificationID/>
</nc:OrganizationIdentification>
</j:CaseCourt>
<!--  DocumentPostDate is required and is the datetime for the request  -->
<nc:DocumentPostDate>
<nc:DateTime>2021-05-24T14:20:47.0Z</nc:DateTime>
</nc:DocumentPostDate>
<!--  PolicyQueryCriteria is required by schema but unused  -->
<policyrequest:PolicyQueryCriteria/>
</policyrequest:GetPolicyRequestMessage>";*/
          var xmlRequest = @"<CourtPolicyQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CourtPolicyQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-CourtPolicyQueryMessage.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID></nc:IdentificationID>
		</nc:OrganizationIdentification>
	</j:CaseCourt>
</CourtPolicyQueryMessage>";
            var xml = XElement.Parse(xmlRequest);
            var ncNamespace = "http://release.niem.gov/niem/niem-core/4.0/";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            //Log.Information("caseCourt {0}", caseCourt);
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            //var documentPostDate = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "DocumentPostDate"))?.FirstOrDefault();
            //var dateTime = documentPostDate.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "DateTime"))?.FirstOrDefault();
            courtIDElement.Value = courtLocation;
            //dateTime.Value = timestamp.ToString("yyyyMMddTHH:mm:ssZ");
            Log.Information("GetPolicy: {0}", xml);
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
                
                var response = service.GetPolicy(xml);
                return response;
            }
        }

        //
        // Request Payment Account List from EFM service
        //
        public EFMFirmService.PaymentAccountListResponseType GetPaymentAccountList(AuthenticateResponseType user)
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
                    //Log.Information(" FirmID = " + p.FirmID?.ToString());
                    //Log.Information(" PaymentAccountTypeCode = " + p.PaymentAccountTypeCode?.ToString());
                    Log.Information(" AccountName = " + p.AccountName?.ToString());
                    //Log.Information(" AccountToken = " + p.AccountToken?.ToString());
                    //Log.Information(" CardType = " + p.CardType?.ToString());
                    //Log.Information(" CardLast4 = " + p.CardLast4?.ToString());
                    //Log.Information(" CardName = " + p.CardHolderName?.ToString());
                    Log.Information(" Active = " + p.Active);
                    //Log.Information("");
                }
                return pmtType;
            }
        }

        public void GetPaymentAccountTypeList(AuthenticateResponseType user)
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
                
                EFMFirmService.PaymentAccountTypeListResponseType response = service.GetPaymentAccountTypeList();
                //Log.Information("PaymentAccountType: {0}", response.PaymentAccountType);
                /*foreach(var paymentAccountType in response.PaymentAccountType)
                {
                    Log.Information("paymentAccountType {0} - {1}", paymentAccountType.Code, paymentAccountType.Description);
                }*/
                //return response;
            }
        }

        /*
        public EFMFirmService.PaymentAccountListResponseType CreatePaymentAccount(AuthenticateResponseType user)
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
                EFMFirmService.CreatePaymentAccountRequestType createPaymentAccountRequestType = new EFMFirmService.CreatePaymentAccountRequestType();
                EFMFirmService.PaymentAccountType paymentAccountType = new EFMFirmService.PaymentAccountType();
                paymentAccountType.
                createPaymentAccountRequestType.PaymentAccount = 
                EFMFirmService.PaymentAccountResponseType pmtType = service.CreatePaymentAccount();

                return pmtType;
            }
        }
        */
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

        public void GetTylerLocationCodes()
        {
            var zipFilePath = ConfigurationManager.AppSettings.Get("zipFile");
            var folderPath = string.Concat(ConfigurationManager.AppSettings.Get("CodeFolder"));
            Log.Information("Code Folder Path: {0}", folderPath);
            var fileNames = new List<string>();
            fileNames.Add("location");
            var urls = new List<string>();
            var zips = new List<string>();
            var xmls = new List<string>();
            Log.Information("For Loop: fileNames size {0} ", fileNames.Count);
            foreach (string fileName in fileNames)
            {
                //Log.Information("Start Loop");
                var url = ConfigurationManager.AppSettings.Get("CourtURL").Trim('\r', '\n') + fileName.ToLower().Trim('\r', '\n');
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
                //Log.Information("End Loop");
            }
            Log.Information("Encoding data");
            var _data = Encoding.UTF8.GetBytes(DateTime.Now.ToString("o"));
            Log.Information("ContentInfo");
            ContentInfo _info = new ContentInfo(_data);
            Log.Information("SignedCms");
            SignedCms _cms = new SignedCms(_info, false);
            CmsSigner _signer = new CmsSigner(this.MessageSigningCertificate);
            Log.Information("CmsSigner {0}", _signer);
            Log.Information("ComputeSignature");
            try
            {
                _cms.ComputeSignature(_signer, false);
            }
            catch (Exception exsig)
            {
                Log.Information("Exception signing: {0}", exsig.Message);
            }
            Log.Information("Signed");
            var _signed = _cms.Encode();
            Log.Information("Convert b64String");
            var _b64 = Convert.ToBase64String(_signed);
            Log.Information("Web Client call");
            using (WebClient _client = new WebClient())
            {
                _client.Headers["tyl-efm-api"] = _b64;
                for (int i = 0; i < urls.Count; i++)
                {
                    Log.Information(urls[i]);
                    Log.Information(zips[i]);
                    try
                    {
                        Log.Information("Try block: downloading");
                        _client.DownloadFile(urls[i], zips[i]);
                    }
                    catch (Exception ex)
                    {
                        Log.Information("exception: message:{0}; source:{1}; stacktrace:{2}", ex.Message, ex.Source, ex.StackTrace);
                        Log.Error(ex.Message + " for url " + urls[i]);
                    }
                }
            }
            Log.Information("extracting zip files");
            for (int i = 0; i < xmls.Count; i++)
            {
                // Download and extract zip file               
                try
                {
                    if (File.Exists(xmls[i]))
                    {
                        Log.Information("deleting previous files from codes directory");
                        File.Delete(xmls[i]);
                    }
                    Log.Information("zip found ready to extract");
                    ZipFile.ExtractToDirectory(zips[i], folderPath);
                    //File.Delete(zips[i]);
                }
                catch (Exception ex)
                {
                    Log.Error("exception: {0}-{1}", ex.Message, ex.InnerException);
                }
            }
        }

        public void GetTylerCodes(string courtID)
        {
            var zipFilePath = ConfigurationManager.AppSettings.Get("zipFile");
            var folderPath = string.Concat(ConfigurationManager.AppSettings.Get("CodeFolder"), @"\" , courtID.Replace(":", ""));
            Log.Information("Code Folder Path: {0}", folderPath);
            var fileNames = new List<string>(ConfigurationManager.AppSettings.Get("fileList").Split(new char[] { ';' }));
            var urls = new List<string>();
            var zips = new List<string>();
            var xmls = new List<string>();
            Log.Information("For Loop: fileNames size {0} ", fileNames.Count);
            foreach (string fileName in fileNames)
            {
                //Log.Information("Start Loop");
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
                //Log.Information("End Loop");
            }
            Log.Information("Encoding data");
            var _data = Encoding.UTF8.GetBytes(DateTime.Now.ToString("o"));
            Log.Information("ContentInfo");
            ContentInfo _info = new ContentInfo(_data);
            Log.Information("SignedCms");
            SignedCms _cms = new SignedCms(_info, false);
            CmsSigner _signer = new CmsSigner(this.MessageSigningCertificate);
            Log.Information("CmsSigner {0}", _signer);
            Log.Information("ComputeSignature");
            try
            {
                _cms.ComputeSignature(_signer, false);
            }catch(Exception exsig)
            {
                Log.Information("Exception signing: {0}", exsig.Message);
            }
            Log.Information("Signed");
            var _signed = _cms.Encode();
            Log.Information("Convert b64String");
            var _b64 = Convert.ToBase64String(_signed);
            Log.Information("Web Client call");
            using (WebClient _client = new WebClient())
            {
                _client.Headers["tyl-efm-api"] = _b64;
                for (int i = 0; i < urls.Count; i++)
                {
                    Log.Information(urls[i]);
                    Log.Information(zips[i]);
                    try
                    {
                        Log.Information("Try block: downloading");
                        _client.DownloadFile(urls[i], zips[i]);
                    }
                    catch (Exception ex)
                    {
                        Log.Information("exception: message:{0}; source:{1}; stacktrace:{2}", ex.Message, ex.Source, ex.StackTrace);
                        Log.Error(ex.Message + " for url " + urls[i]);
                    }
                }
            }
            Log.Information("extracting zip files");
            for (int i = 0; i < xmls.Count; i++)
            {
                // Download and extract zip file               
                try
                {
                    if (File.Exists(xmls[i]))
                    {
                        Log.Information("deleting previous files from codes directory");
                        File.Delete(xmls[i]);
                    }
                    Log.Information("zip found ready to extract");
                    ZipFile.ExtractToDirectory(zips[i], folderPath);
                    //File.Delete(zips[i]);
                }
                catch (Exception ex)
                {
                    Log.Error("exception: {0}-{1}", ex.Message, ex.InnerException);
                }
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

        public XElement GetFilingList(AuthenticateResponseType user, String courtID, String userID, String fromDate, String toDate)
        {
            String requestString = @"<FilingListQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingListQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingListQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-FilingListQueryMessage.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID/>
		</nc:OrganizationIdentification>
	</j:CaseCourt>
	<nc:CaseTrackingID />
	<nc:DocumentSubmitter>
		<ecf:EntityPerson>
			<nc:PersonOtherIdentification>
				<nc:IdentificationID/>
			</nc:PersonOtherIdentification>
		</ecf:EntityPerson>
	</nc:DocumentSubmitter>
	<nc:DateRange>
		<nc:StartDate>
			<nc:Date>2022-04-01</nc:Date>
		</nc:StartDate>
		<nc:EndDate>
			<nc:Date>2022-04-13</nc:Date>
		</nc:EndDate>
	</nc:DateRange>
</FilingListQueryMessage>";

            XElement xml = XElement.Parse(requestString);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var documentSubmitter = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "DocumentSubmitter"))?.FirstOrDefault();
            var identificationID = documentSubmitter.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            courtIDElement.Value = courtID;
            identificationID.Value = userID;

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
                var response = service.GetFilingList(xml);
                return response;
            }
        }

        public XElement GetFilingStatus(AuthenticateResponseType user, String courtID, String documentTrackingID)
        {
            String requestString = @"<FilingStatusQueryMessage xmlns='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusQueryMessage-4.0' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xsi:schemaLocation='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:FilingStatusQueryMessage-4.0 ..\..\..\Schema\message\ECF-4.0-FilingStatusQueryMessage.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingassemblymde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID/>
		</nc:OrganizationIdentification>
	</j:CaseCourt>
	<nc:DocumentIdentification>
		<nc:IdentificationID/>
	</nc:DocumentIdentification>
</FilingStatusQueryMessage>";

            XElement xml = XElement.Parse(requestString);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var documenIdentification = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "DocumentIdentification"))?.FirstOrDefault();
            var documentTracking = documenIdentification.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            courtIDElement.Value = courtID;
            documentTracking.Value = documentTrackingID;
            
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
                Log.Information("GetFilingStatus Request {0}", xml);
                var response = service.GetFilingStatus(xml);
                return response;
            }
        }

        public XElement GetFilingDetails(AuthenticateResponseType user, String courtID, String documentTrackingID)
        {

            String requestString = @"<FilingDetailQueryMessage xmlns='urn: tyler: ecf: extensions: FilingDetailQueryMessage' xmlns:j='http://niem.gov/niem/domains/jxdm/4.0' xmlns:nc='http://niem.gov/niem/niem-core/2.0' xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:ecf='urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:CommonTypes-4.0' xsi:schemaLocation='urn:tyler:ecf:extensions:FilingDetailQueryMessage ..\..\..\Schema\Substitution\FilingDetailQuery.xsd'>
	<ecf:SendingMDELocationID>
		<nc:IdentificationID>https://filingreviewmde.com</nc:IdentificationID>
	</ecf:SendingMDELocationID>
	<ecf:SendingMDEProfileCode>urn:oasis:names:tc:legalxml-courtfiling:schema:xsd:WebServicesMessaging-2.0</ecf:SendingMDEProfileCode>
	<ecf:QuerySubmitter>
		<ecf:EntityPerson />
	</ecf:QuerySubmitter>
	<j:CaseCourt>
		<nc:OrganizationIdentification>
			<nc:IdentificationID />
		</nc:OrganizationIdentification>
	</j:CaseCourt>
	<nc:DocumentIdentification>
		<nc:IdentificationID />
	</nc:DocumentIdentification>
</FilingDetailQueryMessage>";

            XElement xml = XElement.Parse(requestString);
            var ncNamespace = "http://niem.gov/niem/niem-core/2.0";
            var jNamespace = "http://niem.gov/niem/domains/jxdm/4.0";
            var caseCourt = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", jNamespace, "CaseCourt"))?.FirstOrDefault();
            var courtIDElement = caseCourt.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            var documenIdentification = xml.Elements().Where(x => x.Name == string.Format("{{{0}}}{1}", ncNamespace, "DocumentIdentification"))?.FirstOrDefault();
            var documentTracking = documenIdentification.Descendants().Where(x => x.Name.LocalName.ToLower() == "identificationid")?.FirstOrDefault();
            courtIDElement.Value = courtID;
            documentTracking.Value = documentTrackingID;
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
                var response = service.GetFilingDetails(xml);
                return response;
            }
        }

        public XElement GetFeesCalculation(XElement xml, AuthenticateResponseType user)
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
                //Log.Information("1016: Review Filing header: {0}", messageHeader);
                var response = service.GetFeesCalculation(xml);
                return response;
            }
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
                //Log.Information("1016: Review Filing header: {0}", messageHeader);
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
