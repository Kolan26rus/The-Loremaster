﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Styx.Bot.Quest_Behaviors
{
    /// <summary>
    /// InteractWith by Nesox
    /// Allows you to do quests that requires you to interact with nearby objects.
    /// ##Syntax##
    /// QuestId: Id of the quest.
    /// MobId: Id of the object to interact with.
    /// NumOfTimes: Number of times to interact with object.
    /// [Optional]CollectionDistance: The distance it will use to collect objects. DefaultValue:100 yards
    /// ObjectType: the type of object to interact with, expected value: Npc/Gameobject
    /// X,Y,Z: The general location where theese objects can be found
    /// </summary>
    public class InteractWith : CustomForcedBehavior
    {
        public InteractWith(Dictionary<string, string> args)
            : base(args)
        {
            bool error = false;

            uint questId;
            if (!uint.TryParse(Args["QuestId"], out questId))
            {
                Logging.Write("Parsing attribute 'QuestId' in InteractWith behavior failed! please check your profile!");
                error = true;
            }

            uint mobId;
            if (!uint.TryParse(Args["MobId"], out mobId))
            {
                Logging.Write("Parsing attribute 'MobId' in InteractWith behavior failed! please check your profile!");
                error = true;
            }

            int numOfTimes;
            if (!int.TryParse(Args["NumOfTimes"], out numOfTimes))
            {
                Logging.Write("Parsing attribute 'NumOfTimes' in InteractWith behavior failed! please check your profile!");
                error = true;
            }

            if (Args.ContainsKey("CollectionDistance"))
            {
                int distance;
                int.TryParse(Args["CollectionDistance"], out distance);
                CollectionDistance = distance != 0 ? distance : 100;
            }

            if (!Args.ContainsKey("ObjectType"))
            {
                Logging.Write("Could not find attribute 'ObjectType' in InteractWith behavior! please check your profile!");
                error = true;
            }

            var type = (ObjectType)Enum.Parse(typeof(ObjectType), Args["ObjectType"], true);

            float x, y, z;
            if (!float.TryParse(Args["X"], out x))
            {
                Logging.Write("Parsing attribute 'X' in InteractWith behavior failed! please check your profile!");
                error = true;
            }

            if (!float.TryParse(Args["Y"], out y))
            {
                Logging.Write("Parsing attribute 'Y' in InteractWith behavior failed! please check your profile!");
                error = true;
            }

            if (!float.TryParse(Args["Z"], out z))
            {
                Logging.Write("Parsing attribute 'Z' in InteractWith behavior failed! please check your profile!");
                error = true;
            }

            if (error)
                Thread.CurrentThread.Abort();

            ObjectType = type;
            QuestId = questId;
            NumOfTimes = numOfTimes;
            MobId = mobId;
            Location = new WoWPoint(x, y, z);
        }

        public WoWPoint Location { get; private set; }
        public int Counter { get; set; }
        public uint MobId { get; set; }
        public int NumOfTimes { get; set; }
        public uint QuestId { get; private set; }
        public ObjectType ObjectType { get; private set; }
        public int CollectionDistance = 100;

        private readonly List<ulong> _npcBlacklist = new List<ulong>();

        /// <summary> Current object we should interact with.</summary>
        /// <value> The object.</value>
        private WoWObject CurrentObject
        {
            get
            {
                WoWObject @object = null;
                switch (ObjectType)
                {
                    case ObjectType.Gameobject:
                        @object = ObjectManager.GetObjectsOfType<WoWGameObject>().OrderBy(ret => ret.Distance).FirstOrDefault(obj =>
                            !_npcBlacklist.Contains(obj.Guid) &&
                            obj.Distance < CollectionDistance &&
                            obj.Entry == MobId);

                        break;

                    case ObjectType.Npc:
                        @object = ObjectManager.GetObjectsOfType<WoWUnit>().OrderBy(ret => ret.Distance).FirstOrDefault(obj =>
                            !_npcBlacklist.Contains(obj.Guid) &&
                            obj.Distance < CollectionDistance &&
                            obj.Entry == MobId);

                        break;

                }

                if (@object != null)
                {
                    Logging.Write(@object.Name);
                }
                return @object;
            }
        }

        #region Overrides of CustomForcedBehavior

        private Composite _root;
        protected override Composite CreateBehavior()
        {
            return _root ?? (_root =
                new PrioritySelector(

                    new Decorator(ret => Counter >= NumOfTimes,
                        new Action(ret => _isDone = true)),

                        new PrioritySelector(

                            new Decorator(ret => CurrentObject != null && !CurrentObject.WithinInteractRange,
                                new Sequence(
                                    new Action(delegate { TreeRoot.StatusText = "Moving to interact with - " + CurrentObject.Name; }),
                                    new Action(ret => Navigator.MoveTo(CurrentObject.Location))
                                    )
                                ),
                                
                            new Decorator(ret => CurrentObject != null && CurrentObject.WithinInteractRange,
                                new Sequence(
                                    new DecoratorContinue(ret => StyxWoW.Me.IsMoving,
                                        new Action(delegate
                                            {
                                                WoWMovement.MoveStop();
                                                StyxWoW.SleepForLagDuration();
                                            })),

                                        new Action(delegate
                                        {
                                            TreeRoot.StatusText = "Interacting with - " + CurrentObject.Name;
                                            CurrentObject.Interact();
                                            _npcBlacklist.Add(CurrentObject.Guid);

                                            StyxWoW.SleepForLagDuration();
                                            Counter++;
                                            Thread.Sleep(3000);
                                        }))
                                        ),

                            new Sequence(
                                new Action(delegate { Counter++; })
                                )
						)
                ));
        }

        private bool _isDone;
        public override bool IsDone
        {
            get
            {
                PlayerQuest quest = StyxWoW.Me.QuestLog.GetQuestById(QuestId);

                return
                    _isDone ||
                    (quest != null && quest.IsCompleted) ||
                    quest == null;
            }
        }

        public override void OnStart()
        {
            PlayerQuest quest = StyxWoW.Me.QuestLog.GetQuestById(QuestId);
            if (quest != null)
                TreeRoot.GoalText = string.Format("Interacting with Mob Id:{0} {1} Times for quest:{2}", MobId, NumOfTimes, quest.Name);
        }

        #endregion
    }

    public enum ObjectType
    {
        Npc,
        Gameobject
    }
}
