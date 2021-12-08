using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Threading.Tasks;

namespace NotificationService
{
    // The following must be added to the app.Config in order for the MessageInspectorBehaviorExtension to work correctly.
    // <behaviorExtensions>
    //  <add name = "messageBehaviorExtension" type="NotificationService.MessageInspectorBehaviorExtension, NotificationService /*DllName*/, Version=1.0.0.0, Culture=neutral"/>
    // </behaviorExtensions>
    public class MessageInspectorBehaviorExtension : BehaviorExtensionElement, IServiceBehavior
    {
        public override Type BehaviorType
        {
            get
            {
                return typeof(MessageInspectorBehaviorExtension);
            }
        }

        public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, Collection<ServiceEndpoint> endpoints, BindingParameterCollection bindingParameters)
        {
        }

        public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
        {
            for (int index = 0; index < serviceHostBase.ChannelDispatchers.Count; ++index)
            {
                ChannelDispatcher channelDispatcher = serviceHostBase.ChannelDispatchers[index] as ChannelDispatcher;
                if (channelDispatcher != null)
                {
                    foreach (EndpointDispatcher endpoint in channelDispatcher.Endpoints)
                        endpoint.DispatchRuntime.MessageInspectors.Add((IDispatchMessageInspector)new MessageInspector());
                }
            }
        }

        public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase)
        {
        }

        protected override object CreateBehavior()
        {
            return (object)this;
        }
    }
}
