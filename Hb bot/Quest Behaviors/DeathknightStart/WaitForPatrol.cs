﻿// Behavior originally contributed by HighVoltz.
//
// DOCUMENTATION:
//     
//
using System;
using System.Collections.Generic;
using System.Linq;

using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;
using Action = TreeSharp.Action;


namespace Styx.Bot.Quest_Behaviors
{
    /// <summary>
    /// Waits at a safe location until an NPC is X distance way from you.. Useful for the quest in dk starter area where you have to ninja a horse but have to stay away from the stable master
    /// ##Syntax##
    /// MobId: This is the ID of the bad boy you want to stay clear of 
    /// QuestId: (Optional) The Quest to perform this behavior on
    /// Distance: The Distance to stay away from 
    /// X,Y,Z: The Safe Location location where you want wait at.
    /// </summary>
    public class WaitForPatrol : CustomForcedBehavior
    {
        public WaitForPatrol(Dictionary<string, string> args)
            : base(args)
        {
            try
            {
                // QuestRequirement* attributes are explained here...
                //    http://www.thebuddyforum.com/mediawiki/index.php?title=Honorbuddy_Programming_Cookbook:_QuestId_for_Custom_Behaviors
                // ...and also used for IsDone processing.
                AvoidMobId      = GetAttributeAsNullable<int>("AvoidMobId", true, ConstrainAs.MobId, new [] { "MobId" }) ?? 0;
                AvoidDistance   = GetAttributeAsNullable<double>("AvoidDistance", true, ConstrainAs.Range, new [] { "Distance" }) ?? 0;
                QuestId         = GetAttributeAsNullable<int>("QuestId", false, ConstrainAs.QuestId(this), null) ?? 0;
                QuestRequirementComplete = GetAttributeAsNullable<QuestCompleteRequirement>("QuestCompleteRequirement", false, null, null) ?? QuestCompleteRequirement.NotComplete;
                QuestRequirementInLog    = GetAttributeAsNullable<QuestInLogRequirement>("QuestInLogRequirement", false, null, null) ?? QuestInLogRequirement.InLog;
                SafespotLocation = GetAttributeAsNullable<WoWPoint>("", true, ConstrainAs.WoWPointNonEmpty, null) ?? WoWPoint.Empty;
			}

			catch (Exception except)
			{
				// Maintenance problems occur for a number of reasons.  The primary two are...
				// * Changes were made to the behavior, and boundary conditions weren't properly tested.
				// * The Honorbuddy core was changed, and the behavior wasn't adjusted for the new changes.
				// In any case, we pinpoint the source of the problem area here, and hopefully it
				// can be quickly resolved.
				LogMessage("error", "BEHAVIOR MAINTENANCE PROBLEM: " + except.Message
									+ "\nFROM HERE:\n"
									+ except.StackTrace + "\n");
				IsAttributeProblem = true;
			}
        }


        // Attributes provided by caller
        public int                      AvoidMobId { get; private set; }
        public double                   AvoidDistance { get; private set; }  // Distance to stay away from 
        public int                      QuestId { get; private set; }
        public QuestCompleteRequirement QuestRequirementComplete { get; private set; }
        public QuestInLogRequirement    QuestRequirementInLog { get; private set; }
        public WoWPoint                 SafespotLocation { get; private set; }  // Safespot

        // Private variables for internal state
        private bool                _isBehaviorDone;
        private bool                _isDisposed;
        private Composite           _root;

        // Private properties
        private WoWObject       AvoidNpc { get { return (ObjectManager.GetObjectsOfType<WoWUnit>(true)
                                                                      .Where(o => o.Entry == AvoidMobId)
                                                                      .OrderBy(o => o.Distance)
                                                                      .FirstOrDefault()); } }
        private LocalPlayer     Me { get { return (ObjectManager.Me); } }

        // DON'T EDIT THESE--they are auto-populated by Subversion
        public override string      SubversionId { get { return ("$Id: WaitForPatrol.cs 184 2011-06-26 21:59:04Z chinajade $"); } }
        public override string      SubversionRevision { get { return ("$Revision: 184 $"); } }


        ~WaitForPatrol()
        {
            Dispose(false);
        }	

		
		public void     Dispose(bool    isExplicitlyInitiatedDispose)
        {
            if (!_isDisposed)
            {
                // NOTE: we should call any Dispose() method for any managed or unmanaged
                // resource, if that resource provides a Dispose() method.

                // Clean up managed resources, if explicit disposal...
                if (isExplicitlyInitiatedDispose)
                {
                    // empty, for now
                }

                // Clean up unmanaged resources (if any) here...
                TreeRoot.GoalText = string.Empty;
                TreeRoot.StatusText = string.Empty;

                // Call parent Dispose() (if it exists) here ...
                base.Dispose();
            }

            _isDisposed = true;
        }
		

        #region Overrides of CustomForcedBehavior

        protected override Composite CreateBehavior()
        {
            return _root ??(_root = 
                new PrioritySelector(
                    
                    new Decorator(c => Me.Location.Distance(SafespotLocation) > 4,

                        new PrioritySelector(

                            new Decorator(ret => !Me.Mounted && Mount.CanMount() && CharacterSettings.Instance.UseMount && Me.Location.Distance(SafespotLocation) > 35,
                                new Sequence(
                                    new DecoratorContinue(ret => Me.IsMoving,
                                        new Sequence(
                                            new Action(ret => WoWMovement.MoveStop()),
                                            new Action(ret => StyxWoW.SleepForLagDuration()) 
                                                )),

                                    new Action(ret => Mount.MountUp()))),

                            new Action(ret => Navigator.MoveTo(SafespotLocation)))),
                                    
                            new Decorator(c => AvoidNpc != null && AvoidNpc.Distance <= AvoidDistance,
                                new Action(c => LogMessage("info", "Waiting on {0} to move {1} distance away", AvoidNpc, AvoidDistance))),

                            new Decorator(c => AvoidNpc == null || AvoidNpc.Distance > AvoidDistance,
                                new Action(c => _isBehaviorDone = true))
                            )
                );
        }


        public override void    Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        public override bool IsDone
        {
            get
            {
                return (_isBehaviorDone     // normal completion
                        || !UtilIsProgressRequirementsMet(QuestId, QuestRequirementInLog, QuestRequirementComplete));
            }
        }


        public override void OnStart()
        {
            // This reports problems, and stops BT processing if there was a problem with attributes...
            // We had to defer this action, as the 'profile line number' is not available during the element's
            // constructor call.
            OnStart_HandleAttributeProblem();

            // If the quest is complete, this behavior is already done...
            // So we don't want to falsely inform the user of things that will be skipped.
            if (!IsDone)
            {
                PlayerQuest quest = StyxWoW.Me.QuestLog.GetQuestById((uint)QuestId);

                TreeRoot.GoalText = this.GetType().Name + ": " + ((quest != null) ? ("\"" + quest.Name + "\"") : "In Progress");
                TreeRoot.StatusText = string.Format("Moving to safepoint {0} until MobId({1}) moves {2} yards away.",
                                                    SafespotLocation,
                                                    AvoidMobId,
                                                    AvoidDistance);
            }
        }

        #endregion
    }
}
