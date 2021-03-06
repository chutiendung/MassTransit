﻿// Copyright 2007-2016 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit
{
    using System;
    using ConsumeConfigurators;
    using ConsumeConnectors;
    using GreenPipes;
    using Pipeline;


    /// <summary>
    /// Extensions for subscribing object instances.
    /// </summary>
    public static class InstanceExtensions
    {
        /// <summary>
        /// Subscribes an object instance to the bus
        /// </summary>
        /// <param name="configurator">Service Bus Service Configurator 
        ///     - the item that is passed as a parameter to
        ///     the action that is calling the configurator.</param>
        /// <param name="instance">The instance to subscribe.</param>
        /// <returns>An instance subscription configurator.</returns>
        public static void Instance(this IReceiveEndpointConfigurator configurator, object instance)
        {
            var instanceConfigurator = new InstanceConfigurator(instance);

            configurator.AddEndpointSpecification(instanceConfigurator);
        }

        /// <summary>
        /// Subscribes an object instance to the bus
        /// </summary>
        /// <param name="configurator">Service Bus Service Configurator 
        ///     - the item that is passed as a parameter to
        ///     the action that is calling the configurator.</param>
        /// <param name="instance">The instance to subscribe.</param>
        /// <param name="configure">Configure the instance</param>
        /// <returns>An instance subscription configurator.</returns>
        public static void Instance<T>(this IReceiveEndpointConfigurator configurator, T instance, Action<IInstanceConfigurator<T>> configure = null)
            where T : class, IConsumer
        {
            var instanceConfigurator = new InstanceConfigurator<T>(instance);

            configure?.Invoke(instanceConfigurator);

            configurator.AddEndpointSpecification(instanceConfigurator);
        }

        /// <summary>
        /// Connects any consumers for the object to the message dispatcher
        /// </summary>
        /// <param name="bus">The service bus to configure</param>
        /// <param name="instance"></param>
        /// <returns>The unsubscribe action that can be called to unsubscribe the instance
        /// passed as an argument.</returns>
        public static ConnectHandle ConnectInstance(this IConsumePipeConnector bus, object instance)
        {
            if (bus == null)
                throw new ArgumentNullException(nameof(bus));
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            var connector = InstanceConnectorCache.GetInstanceConnector(instance.GetType());

            return connector.ConnectInstance(bus, instance);
        }

        /// <summary>
        /// Connects any consumers for the object to the message dispatcher
        /// </summary>
        /// <typeparam name="T">The consumer type</typeparam>
        /// <param name="bus">The service bus instance to call this method on.</param>
        /// <param name="instance">The instance to subscribe.</param>
        /// <returns>The unsubscribe action that can be called to unsubscribe the instance
        /// passed as an argument.</returns>
        public static ConnectHandle ConnectInstance<T>(this IConsumePipeConnector bus, T instance)
            where T : class, IConsumer
        {
            var connector = InstanceConnectorCache.GetInstanceConnector<T>();

            return connector.ConnectInstance(bus, instance);
        }
    }
}