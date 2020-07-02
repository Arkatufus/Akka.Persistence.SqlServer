//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryExampleActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Persistence;

namespace PersistenceExample
{
    public class Message
    {
        public Message(string data)
        {
            Data = data;
        }

        public string Data { get; private set; }
    }

    public class Confirmable
    {
        public Confirmable(long deliveryId, string data)
        {
            DeliveryId = deliveryId;
            Data = data;
        }


        public long DeliveryId { get; private set; }

        public string Data { get; private set; }
    }

    public class Confirmation
    {
        public Confirmation(long deliveryId)
        {
            DeliveryId = deliveryId;
        }

        public long DeliveryId { get; private set; }
    }

    [Serializable]
    public class Snap
    {
        public Snap(AtLeastOnceDeliverySnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public AtLeastOnceDeliverySnapshot Snapshot { get; private set; }
    }

    public class DeliveryActor : UntypedActor
    {
        bool Confirming = true;

        protected override void OnReceive(object message)
        {
            switch(message)
            {
                case string str when str == "start":
                    Confirming = true;
                    break;
                case string str when str == "stop":
                    Confirming = false;
                    break;
                case Confirmable msg:
                    if (Confirming)
                    {
                        Console.WriteLine("Confirming delivery of message id: {0} and data: {1}", msg.DeliveryId, msg.Data);
                        Context.Sender.Tell(new Confirmation(msg.DeliveryId));
                    }
                    else
                    {
                        Console.WriteLine("Ignoring message id: {0} and data: {1}", msg.DeliveryId, msg.Data);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// AtLeastOnceDelivery will repeat sending messages, unless confirmed by deliveryId
    /// 
    /// By default, in-memory Journal is used, so this won't survive system restarts. 
    /// </summary>
    public class AtLeastOnceDeliveryExampleActor : AtLeastOnceDeliveryActor
    {
        public ActorPath DeliveryPath { get; }

        public AtLeastOnceDeliveryExampleActor(ActorPath deliveryPath)
        {
            DeliveryPath = deliveryPath;
        }

        public override string PersistenceId
        {
            get { return "at-least-once-1"; }
        }

        protected override bool ReceiveRecover(object message)
        {
            switch(message)
            {
                case Message msg:
                    var messageData = msg.Data;
                    Console.WriteLine("recovered {0}", messageData);
                    Deliver(DeliveryPath,
                            id =>
                            {
                                Console.WriteLine("recovered delivery task: {0}, with deliveryId: {1}", messageData, id);
                                return new Confirmable(id, messageData);
                            });
                    return true;
                case Confirmation c:
                    var deliveryId = c.DeliveryId;
                    Console.WriteLine("recovered confirmation of {0}", deliveryId);
                    ConfirmDelivery(deliveryId);
                    return true;
                default:
                    return false;
            }
        }

        protected override bool ReceiveCommand(object message)
        {
            switch(message)
            {
                //case string s when s == "boom":
                //    throw new Exception("Controlled devastation");
                case Message msg:
                    Persist(msg, m =>
                    {
                        Deliver(DeliveryPath,
                            id =>
                            {
                                Console.WriteLine("sending: {0}, with deliveryId: {1}", m.Data, id);
                                return new Confirmable(id, m.Data);
                            });
                    });
                    return true;

                case Confirmation c:
                    Persist(c, m => ConfirmDelivery(m.DeliveryId));
                    return true;

                default:
                    return false;
            }
        }
    }
}
