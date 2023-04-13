// -------------------------------------------------------------------------------------------------------------------------------
// FilingAssemblyMDEPort - Filing Notification Service - City of Fresno
// R.Short - BitLink 08/20/18
//
// Purpose: The NotificationService will process all aSync filing acceptance/reject notifications sent from the NFRC system
//          and will send a json formatted message containing the notification back to ePros via the REST services.
// -------------------------------------------------------------------------------------------------------------------------------

using System.Runtime.Serialization;
using System.ServiceModel;
using System.Xml.Linq;

namespace NotificationService
{
    // Odyssey File & Serve Mtom streaming notification contract
    //[ServiceContract(Name = "FilingAssemblyMDEPort", Namespace = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0")]
    [ServiceContract(Name = "FilingAssemblyMDEPort", Namespace = "https://docs.oasis-open.org/legalxml-courtfiling/ns/v5.0WSDL/FilingAssemblyMDE")]
    public interface IFilingAssemblyMDEPort
    {
        // Interface operation contract to Odyssey File & Serve Mtom streaming notification contracts
        //[OperationContract(ReplyAction = "*", Action = "urn:oasis:names:tc:legalxml-courtfiling:wsdl:WebServicesProfile-Definitions-4.0/FilingAssemblyMDEPort/NotifyFilingReviewCompleteRequest")]
        /*[OperationContract(ReplyAction = "*", Action = "https://docs.oasis-open.org/legalxml-courtfiling/ns/v5.0WSDL/FilingAssemblyMDE/NotifyFilingReviewComplete")]
        NotifyFilingReviewCompleteResponse NotifyFilingReviewComplete(NotifyFilingReviewCompleteRequest request);
        */
    }

}
