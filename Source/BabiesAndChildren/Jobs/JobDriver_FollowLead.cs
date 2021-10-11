﻿using Verse;
using Verse.AI;
using RimWorld;
using System.Collections.Generic;
using System.Diagnostics;
using BabiesAndChildren.api;
using BabiesAndChildren.Tools;

namespace BabiesAndChildren
{
    public class WorkGiver_FollowLead : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        HashSet<JobDef> watchedJobs = new HashSet<JobDef> { JobDefOf.Hunt, JobDefOf.Sow, JobDefOf.Harvest, JobDefOf.Train, JobDefOf.Tame, JobDefOf.TendPatient, JobDefOf.Research };

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!BnCSettings.watchworktype_enabled)
            {
                return false;
            }
            Pawn Mentor = (Pawn) thing;
            if (Mentor == null || Mentor == pawn || Mentor.NonHumanlikeOrWildMan())
            {
                return false;
            }

            if (!RaceUtility.PawnUsesChildren(pawn) || AgeStages.IsOlderThan(pawn, AgeStages.Teenager) || AgeStages.IsYoungerThan(Mentor, AgeStages.Teenager))
            {
                return false;
            }

            if (!Mentor.IsColonist || Mentor.mindState.IsIdle || Mentor.CurJob == null)
            {
                return false;
            }

            if (Mentor.CurJob.bill != null && Mentor.CurJob.bill.recipe.workSkill != null && pawn.skills.GetSkill(Mentor.CurJob.bill.recipe.workSkill).TotallyDisabled)
            {
                return false;
            }

            if (Mentor.CurJob.bill == null && !watchedJobs.Contains(Mentor.CurJobDef))
                return false;

            Pawn PMentor = pawn.TryGetComp<Growing_Comp>().mentor;
            if (PMentor != null && pawn.TryGetComp<Growing_Comp>().onlyMentor && Mentor != PMentor) {
                return false;
            }

            if (FeedPatientUtility.IsHungry(pawn))
            {
                return false;
            }

            if (!pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly, 3))
            {
                return false;
            }

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn pawn2 = (Pawn) t;
            if (pawn2 == null) return null;
            var comp = pawn.TryGetComp<Growing_Comp>();
            if (comp != null && comp.mentor != null)
            {
                if (comp.mentor.CurJob != null && (comp.mentor.CurJob.bill != null ? true : watchedJobs.Contains(comp.mentor.CurJobDef)))
                {
                    pawn2 = comp.mentor;
                }


            }
            return new Job(BnCJobDefOf.BnC_Watch)
            {
                targetA = pawn2,
            };


        }
    }

    public class JobDriver_FollowLead : JobDriver
    {
        protected Pawn Mentor => (Pawn) TargetA.Thing;
        //HashSet<JobDef> watchedJobs = new HashSet<JobDef> { JobDefOf.Hunt, JobDefOf.Sow };
        Dictionary<JobDef, SkillDef> watchedJobs = new Dictionary<JobDef, SkillDef> { {JobDefOf.Hunt, SkillDefOf.Shooting }, {JobDefOf.Sow, SkillDefOf.Plants }, { JobDefOf.Harvest, SkillDefOf.Plants }, { JobDefOf.Train, SkillDefOf.Animals }, { JobDefOf.Tame, SkillDefOf.Animals }, { JobDefOf.TendPatient, SkillDefOf.Medicine }, { JobDefOf.Research, SkillDefOf.Intellectual } };
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOnMentalState(TargetIndex.A);
            this.FailOnNotAwake(TargetIndex.A);
            this.FailOn(() => !Mentor.IsColonist || Mentor.mindState.IsIdle || Mentor.CurJob == null);
            yield return Toils_Reserve.Reserve(TargetIndex.A, 3, 0, null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            yield return MakeWatchToil(Mentor);
            yield break;
            
        }

        
        protected Toil MakeWatchToil(Pawn friend)
        {
            var toil = new Toil();
            SkillDef workSkill = null;
            float mentorTotalTeachPower = 1f;
            toil.initAction = delegate
            {
                if (Mentor.CurJob.bill != null)
                {
                    if (Mentor.CurJob.bill.recipe.workSkill != null)
                    {
                        workSkill = Mentor.CurJob.bill.recipe.workSkill;
                        mentorTotalTeachPower = Mentor.skills.GetSkill(workSkill).Level + ((Mentor.skills.GetSkill(SkillDefOf.Social).Level + Mentor.skills.GetSkill(SkillDefOf.Intellectual).Level) * 0.5f);
                    }
                }else if (watchedJobs.ContainsKey(Mentor.CurJobDef))
                {
                    workSkill = watchedJobs.TryGetValue(Mentor.CurJobDef);
                    mentorTotalTeachPower = Mentor.skills.GetSkill(workSkill).Level + ((Mentor.skills.GetSkill(SkillDefOf.Social).Level + Mentor.skills.GetSkill(SkillDefOf.Intellectual).Level) * 0.5f);
                }
            };
            toil.tickAction = delegate
            {
                var actor = toil.actor;
                bool flag6 = (actor.Position - friend.Position).LengthHorizontalSquared >= 30 || !GenSight.LineOfSight(actor.Position, friend.Position, actor.Map, true, null, 0, 0);
                if (flag6)
                {
                    Job newJob = JobMaker.MakeJob(JobDefOf.Goto, friend);
                    actor.jobs.StartJob(newJob, JobCondition.InterruptForced, null, false, true, null, null, false, false);
                    return;

                }
                else
                {
                    if ((actor.Position - friend.Position).LengthHorizontalSquared <= 6)
                        actor.Rotation = friend.Rotation;
                    else
                        actor.rotationTracker.FaceTarget(friend);

                    if (workSkill != null)
                    {
                        // todo: make it better
                        float xp = 1f;
                        float mentorSkillModifier = Mentor.skills.GetSkill(workSkill).Level / 100f;
                        xp *= mentorSkillModifier;
                        xp *= BnCSettings.watchexpgainmultiplier;

                        actor.skills.Learn(workSkill, xp, false);
                    }

                }
            };
            toil.AddFinishAction(delegate
            {
                
                toil.actor.skills.Learn(workSkill, 10f * mentorTotalTeachPower * BnCSettings.watchexpgainmultiplier, false);
            });
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            toil.handlingFacing = true;
            return toil;
        }
    }
}