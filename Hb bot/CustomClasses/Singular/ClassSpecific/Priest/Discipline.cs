﻿#region Revision Info

// This file is part of Singular - A community driven Honorbuddy CC
// $Author: exemplar $
// $Date: 2011-04-14 11:12:40 +0200 (to, 14 apr 2011) $
// $HeadURL: http://svn.apocdev.com/singular/tags/v1/Singular/ClassSpecific/Priest/Discipline.cs $
// $LastChangedBy: exemplar $
// $LastChangedDate: 2011-04-14 11:12:40 +0200 (to, 14 apr 2011) $
// $LastChangedRevision: 281 $
// $Revision: 281 $

#endregion

using System;
using System.Collections.Generic;
using System.Linq;

using Singular.Settings;

using Styx.Combat.CombatRoutine;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

using TreeSharp;

using Action = TreeSharp.Action;

namespace Singular
{
    partial class SingularRoutine
    {
        public List<WoWPlayer> ResurrectablePlayers
        {
            get
            {
                return ObjectManager.GetObjectsOfType<WoWPlayer>().Where(
                    p => !p.IsMe && p.Dead && p.IsFriendly && p.IsInMyPartyOrRaid &&
                         p.DistanceSqr < 40 * 40 && !Blacklist.Contains(p.Guid)).ToList();
            }
        }

        [Class(WoWClass.Priest)]
        [Spec(TalentSpec.DisciplineHealingPriest)]
        [Spec(TalentSpec.DisciplinePriest)]
        [Behavior(BehaviorType.Rest)]
        [Context(WoWContext.All)]
        public Composite CreateDiscHealRest()
        {
            return new PrioritySelector(
                CreateWaitForCast(),
                // Heal self before resting. There is no need to eat while we have 100% mana
                CreateDiscHealOnlyBehavior(true),
                // Rest up damnit! Do this first, so we make sure we're fully rested.
                CreateDefaultRestComposite(SingularSettings.Instance.DefaultRestHealth, SingularSettings.Instance.DefaultRestMana),
                // Make sure we're healing OOC too!
                CreateDiscHealOnlyBehavior(),
                // Can we res people?
                new Decorator(
                    ret => ResurrectablePlayers.Count != 0,
                    new Sequence(
                        CreateSpellCast("Resurrection", ret => true, ret => ResurrectablePlayers.FirstOrDefault()),
                        new Action(ret => Blacklist.Add(ResurrectablePlayers.FirstOrDefault().Guid, TimeSpan.FromSeconds(15)))
                        ))
                );
        }

        private Composite CreateDiscHealOnlyBehavior()
        {
            return CreateDiscHealOnlyBehavior(false);
        }

        private Composite CreateDiscHealOnlyBehavior(bool selfOnly)
        {
            // Atonement - Tab 1  index 10 - 1/2 pts
            NeedHealTargeting = true;
            return new
                Decorator(
                ret => HealTargeting.Instance.FirstUnit != null,
                new PrioritySelector(
                    ctx => selfOnly ? Me : HealTargeting.Instance.FirstUnit,
                    CreateWaitForCast(),
                    // Ensure we're in range of the unit to heal, and it's in LOS.
                    //CreateMoveToAndFace(35f, ret => (WoWUnit)ret),
                    //CreateSpellBuff("Renew", ret => HealTargeting.Instance.TargetList.FirstOrDefault(u => !u.HasAura("Renew") && u.HealthPercent < 90) != null, ret => HealTargeting.Instance.TargetList.FirstOrDefault(u => !u.HasAura("Renew") && u.HealthPercent < 90)),
                    CreateSpellBuff(
                        "Power Word: Shield", ret => !((WoWUnit)ret).HasAura("Weakened Soul") && ((WoWUnit)ret).Combat, ret => (WoWUnit)ret),
                    new Decorator(
                        ret =>
                        NearbyFriendlyPlayers.Count(p => !p.Dead && p.HealthPercent < SingularSettings.Instance.Priest.PrayerOfHealing) >
                        SingularSettings.Instance.Priest.PrayerOfHealingCount &&
                        (SpellManager.CanCast("Prayer of Healing") || SpellManager.CanCast("Divine Hymn")),
                        new Sequence(
                            CreateSpellCast("Archangel"),
                            // This will skip over DH if we can't cast it.
                            // If we can, the sequence fails, since PoH can't be cast (as we're still casting at this point)
                            new DecoratorContinue(
                                ret => SpellManager.CanCast("Divine Hymn"),
                                CreateSpellCast("Divine Hymn")),
                            CreateSpellCast("Prayer of Healing"))),
                    CreateSpellBuff(
                        "Pain Supression", ret => ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.PainSuppression, ret => (WoWUnit)ret),
                    CreateSpellBuff("Penance", ret => ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.Penance, ret => (WoWUnit)ret),
                    CreateSpellCast(
                        "Flash Heal", ret => ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.FlashHeal, ret => (WoWUnit)ret),
                    CreateSpellCast(
                        "Binding Heal",
                        ret =>
                        (WoWUnit)ret != Me && ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.BindingHealThem &&
                        Me.HealthPercent < SingularSettings.Instance.Priest.BindingHealMe,
                        ret => (WoWUnit)ret),
                    CreateSpellCast(
                        "Greater Heal", ret => ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.GreaterHeal, ret => (WoWUnit)ret),
                    CreateSpellCast("Heal", ret => ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.Heal, ret => (WoWUnit)ret),
                    CreateSpellBuff("Renew", ret => ((WoWUnit)ret).HealthPercent < SingularSettings.Instance.Priest.Renew, ret => (WoWUnit)ret),
                    CreateSpellBuff("Prayer of Mending", ret => ((WoWUnit)ret).HealthPercent < 90, ret => (WoWUnit)ret)

                    // Divine Hymn
                    // Desperate Prayer
                    // Prayer of Mending
                    // Prayer of Healing
                    // Power Word: Barrier
                    // TODO: Add smite healing. Only if Atonement is talented. (Its useless otherwise)
                    ));
        }

        [Class(WoWClass.Priest)]
        [Spec(TalentSpec.DisciplineHealingPriest)]
        [Spec(TalentSpec.DisciplinePriest)]
        [Behavior(BehaviorType.Heal)]
        [Context(WoWContext.All)]
        public Composite CreateDiscHeaComposite()
        {
            return new PrioritySelector(
                // Firstly, deal with healing people!
                CreateDiscHealOnlyBehavior());
        }

        // This behavior is used in combat/heal AND pull. Just so we're always healing our party.
        // Note: This will probably break shit if we're solo, but oh well!
        [Class(WoWClass.Priest)]
        [Spec(TalentSpec.DisciplineHealingPriest)]
        [Spec(TalentSpec.DisciplinePriest)]
        [Behavior(BehaviorType.Combat)]
        [Behavior(BehaviorType.Pull)]
        [Context(WoWContext.All)]
        public Composite CreateDiscCombatComposite()
        {
            return new PrioritySelector(
                //Pull stuff
                new Decorator(
                    ret => !Me.IsInParty && !Me.Combat,
                    new PrioritySelector(
                        CreateEnsureTarget(),
                        CreateMoveToAndFace(28f, ret => Me.CurrentTarget),
                        CreateSpellCast("Holy Fire", ret => !Me.IsInParty && !Me.Combat),
                        CreateSpellCast("Smite", ret => !Me.IsInParty && !Me.Combat)
                        )),
                // If we have nothing to heal, and we're in combat (or the leader is)... kill something!
                new Decorator(
                    ret => Me.Combat || (RaFHelper.Leader != null && RaFHelper.Leader.Combat),
                    new PrioritySelector(
                        CreateEnsureTarget(),
                        CreateMoveToAndFace(39f, ret => Me.CurrentTarget),
                        CreateSpellBuff("Shadow Word: Pain", ret => !Me.IsInParty || Me.ManaPercent >= SingularSettings.Instance.Priest.DpsMana),
                        //Solo combat rotation
                        new Decorator(
                            ret => !Me.IsInParty,
                            new PrioritySelector(
                                CreateSpellCast("Holy Fire"),
                                CreateSpellCast("Penance"))),
                        //Don't smite while mana is below the setting while in a party (default 70)
                        CreateSpellCast("Smite", ret => !Me.IsInParty || Me.ManaPercent >= SingularSettings.Instance.Priest.DpsMana)
                        ))
                );
        }
    }
}