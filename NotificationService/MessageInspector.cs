using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

// This inspector class is used for the NotificationService side of the OFS host service. The wrapper tags must be removed in order
// for the notifcation messageContract to properly consume the message.
namespace NotificationService
{
    public class MessageInspector : IDispatchMessageInspector
    {
        private static XNamespace NsEnvelope = XNamespace.Get("http://schemas.xmlsoap.org/soap/envelope/");
        private XName Body = MessageInspector.NsEnvelope + nameof(Body);
        private XName Envelope = MessageInspector.NsEnvelope + nameof(Envelope);
        private const int BUFFER_SIZE = 2147483647;
        
        public object AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext)
        {
            if (!request.IsEmpty && !request.IsFault)
                request = this.TransformRequest(request);
            return (object)null;
        }

        public void BeforeSendReply(ref Message reply, object correlationState)
        {
            if (reply.IsEmpty || reply.IsFault)
                return;
            this.TransformReply(ref reply);
        }

        private Message TransformRequest(Message request)
        {
            try
            {
                XElement xelement1 = XElement.Load(request.GetReaderAtBodyContents().ReadSubtree());
                XName name = XName.Get(xelement1.Name.LocalName, xelement1.Name.Namespace.NamespaceName);
                XElement xelement2 = new XElement(name);
                XElement xelement3 = new XElement(name);
                xelement3.Add((object)xelement1);
                MemoryStream memoryStream = new MemoryStream();
                xelement3.Save((Stream)memoryStream);
                memoryStream.Position = 0L;
                XmlDictionaryReader textReader = XmlDictionaryReader.CreateTextReader((Stream)memoryStream, this.GetReaderQuotas());
                Message message = Message.CreateMessage(request.Version, "", textReader);
                message.Headers.CopyHeadersFrom(request.Headers);
                message.Properties.CopyProperties(request.Properties);
                return message;
            }
            catch (Exception fault)
            {
                // If a validation error occurred, the message is replaced with the validation fault.  
                request = Message.CreateMessage(request.Version, fault.Message, request.Headers.Action);
                return request;
            }
        }

        private void TransformReply(ref Message reply)
        {
            try
            {
                XPathNavigator navigator = reply.CreateBufferedCopy(int.MaxValue).CreateNavigator();
                MemoryStream memoryStream = new MemoryStream();
                XmlWriter writer = XmlWriter.Create((Stream)memoryStream);
                navigator.WriteSubtree(writer);
                writer.Flush();
                writer.Close();
                memoryStream.Position = 0L;
                XElement element = XElement.Load(XmlReader.Create((Stream)memoryStream));
                XElement xelement1 = element.Element(this.Body);
                if (xelement1 == null)
                    return;
                XElement xelement2 = xelement1.Elements().FirstOrDefault<XElement>();
                xelement1.RemoveAll();
                xelement1.Add((object)xelement2.Elements().FirstOrDefault<XElement>());
                Message message = Message.CreateMessage(element.CreateReader(), int.MaxValue, reply.Version);
                message.Properties.CopyProperties(reply.Properties);
                reply = message;
            }
            catch (Exception fault)
            {
                // If a validation error occurred, the message is replaced with the validation fault.  
                reply = Message.CreateMessage(reply.Version, fault.Message, reply.Headers.Action);
            }
        }

        private XmlDictionaryReaderQuotas GetReaderQuotas()
        {
            return new XmlDictionaryReaderQuotas()
            {
                MaxArrayLength = int.MaxValue,
                MaxBytesPerRead = int.MaxValue,
                MaxDepth = int.MaxValue,
                MaxNameTableCharCount = int.MaxValue,
                MaxStringContentLength = int.MaxValue
            };
        }
    }
}