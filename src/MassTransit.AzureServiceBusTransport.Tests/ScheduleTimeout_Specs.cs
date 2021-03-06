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
namespace MassTransit.AzureServiceBusTransport.Tests
{
    namespace ScheduleTimeout_Specs
    {
        using System;
        using System.Threading.Tasks;
        using Automatonymous;
        using MassTransit.Saga;
        using NUnit.Framework;
        using TestFramework;


        [TestFixture]
        public class Scheduling_a_message_from_a_state_machine :
            AzureServiceBusTestFixture
        {
            [Test, Explicit]
            public async Task Should_cancel_when_the_order_is_submitted()
            {
                CartItemAdded cartItemAdded = new CartItemAddedCommand
                {
                    MemberNumber = Guid.NewGuid().ToString()
                };

                await InputQueueSendEndpoint.Send(cartItemAdded);

                Guid? saga = await _repository.ShouldContainSaga(x => x.MemberNumber == cartItemAdded.MemberNumber
                    && GetCurrentState(x) == _machine.Active, TestTimeout);
                Assert.IsTrue(saga.HasValue);

                await InputQueueSendEndpoint.Send(new OrderSubmittedEvent(cartItemAdded.MemberNumber));

                ConsumeContext<CartRemoved> removed = await _cartRemoved;

                await Task.Delay(3000);
            }

            [Test, Explicit]
            public async Task Should_receive_the_timeout()
            {
                CartItemAdded cartItemAdded = new CartItemAddedCommand
                {
                    MemberNumber = Guid.NewGuid().ToString()
                };

                await InputQueueSendEndpoint.Send(cartItemAdded);

                ConsumeContext<CartRemoved> removed = await _cartRemoved;
            }

            [Test, Explicit]
            public async Task Should_reschedule_the_timeout_when_items_are_added()
            {
                CartItemAdded cartItemAdded = new CartItemAddedCommand
                {
                    MemberNumber = Guid.NewGuid().ToString()
                };

                await InputQueueSendEndpoint.Send(cartItemAdded);

                Guid? saga = await _repository.ShouldContainSaga(x => x.MemberNumber == cartItemAdded.MemberNumber
                    && GetCurrentState(x) == _machine.Active, TestTimeout);
                Assert.IsTrue(saga.HasValue);

                await InputQueueSendEndpoint.Send(cartItemAdded);

                ConsumeContext<CartRemoved> removed = await _cartRemoved;
            }

            InMemorySagaRepository<TestState> _repository;
            TestStateMachine _machine;
            Task<ConsumeContext<CartRemoved>> _cartRemoved;

            State GetCurrentState(TestState state)
            {
                return _machine.GetState(state).Result;
            }

            protected override void ConfigureBusHost(IServiceBusBusFactoryConfigurator configurator, IServiceBusHost host)
            {
                base.ConfigureBus(configurator);

                configurator.UseServiceBusMessageScheduler();

                configurator.SubscriptionEndpoint<CartRemoved>(host, "second_queue", x =>
                {
                    _cartRemoved = Handled<CartRemoved>(x);
                });
            }

            protected override void ConfigureInputQueueEndpoint(IServiceBusReceiveEndpointConfigurator configurator)
            {
                base.ConfigureInputQueueEndpoint(configurator);

                _repository = new InMemorySagaRepository<TestState>();

                _machine = new TestStateMachine();

                configurator.StateMachineSaga(_machine, _repository);
            }
        }


        class TestState :
            SagaStateMachineInstance
        {
            public State CurrentState { get; set; }

            public string MemberNumber { get; set; }

            public Guid? CartTimeoutTokenId { get; set; }

            public int ExpiresAfterSeconds { get; set; }

            public Guid CorrelationId { get; set; }
        }


        class CartItemAddedCommand :
            CartItemAdded
        {
            public string MemberNumber { get; set; }
        }


        public interface CartItemAdded
        {
            string MemberNumber { get; }
        }


        public interface CartRemoved
        {
            string MemberNumber { get; }
        }


        class CartRemovedEvent :
            CartRemoved
        {
            readonly TestState _state;

            public CartRemovedEvent(TestState state)
            {
                _state = state;
            }

            public string MemberNumber
            {
                get { return _state.MemberNumber; }
            }
        }


        class CartExpiredEvent :
            CartExpired
        {
            readonly TestState _state;

            public CartExpiredEvent(TestState state)
            {
                _state = state;
            }

            public string MemberNumber
            {
                get { return _state.MemberNumber; }
            }
        }


        public interface CartExpired
        {
            string MemberNumber { get; }
        }


        class OrderSubmittedEvent :
            OrderSubmitted
        {
            readonly string _memberNumber;

            public OrderSubmittedEvent(string memberNumber)
            {
                _memberNumber = memberNumber;
            }

            public string MemberNumber
            {
                get { return _memberNumber; }
            }
        }


        public interface OrderSubmitted
        {
            string MemberNumber { get; }
        }


        class TestStateMachine :
            MassTransitStateMachine<TestState>
        {
            public TestStateMachine()
            {
                Event(() => ItemAdded, x => x.CorrelateBy(p => p.MemberNumber, p => p.Message.MemberNumber)
                    .SelectId(context => NewId.NextGuid()));

                Event(() => Submitted, x => x.CorrelateBy(p => p.MemberNumber, p => p.Message.MemberNumber));

                Schedule(() => CartTimeout, x => x.CartTimeoutTokenId, x =>
                {
                    x.Delay = TimeSpan.FromSeconds(30);
                    x.Received = p => p.CorrelateBy(state => state.MemberNumber, context => context.Message.MemberNumber);
                });


                Initially(
                    When(ItemAdded)
                        .ThenAsync(context =>
                        {
                            context.Instance.MemberNumber = context.Data.MemberNumber;
                            context.Instance.ExpiresAfterSeconds = 3;
                            return Console.Out.WriteLineAsync($"Cart {context.Instance.CorrelationId} Created: {context.Data.MemberNumber}");
                        })
                        .Schedule(CartTimeout, context => new CartExpiredEvent(context.Instance),
                            context => TimeSpan.FromSeconds(context.Instance.ExpiresAfterSeconds))
                        .TransitionTo(Active));

                During(Active,
                    When(CartTimeout.Received)
                        .ThenAsync(context => Console.Out.WriteLineAsync($"Cart Expired: {context.Data.MemberNumber}"))
                        .Publish(context => (CartRemoved)new CartRemovedEvent(context.Instance))
                        .Finalize(),
                    When(Submitted)
                        .ThenAsync(context => Console.Out.WriteLineAsync($"Cart Submitted: {context.Data.MemberNumber}"))
                        .Unschedule(CartTimeout)
                        .Publish(context => (CartRemoved)new CartRemovedEvent(context.Instance))
                        .Finalize(),
                    When(ItemAdded)
                        .ThenAsync(context => Console.Out.WriteLineAsync($"Card item added: {context.Data.MemberNumber}"))
                        .Schedule(CartTimeout, context => new CartExpiredEvent(context.Instance),
                            context => TimeSpan.FromSeconds(context.Instance.ExpiresAfterSeconds)));

                SetCompletedWhenFinalized();
            }

            public Schedule<TestState, CartExpired> CartTimeout { get; private set; }

            public Event<CartItemAdded> ItemAdded { get; private set; }
            public Event<OrderSubmitted> Submitted { get; private set; }

            public State Active { get; private set; }
        }
    }
}