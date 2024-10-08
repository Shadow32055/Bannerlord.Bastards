﻿using Bastards.StaticUtils;
using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace Bastards.Models {
    public class Bastard
    {
        [SaveableField(1)]
        public Hero? hero = null;
        [SaveableField(2)]
        public Hero father;
        [SaveableField(3)]
        public Hero mother;
        [SaveableField(4)]
        public double birthTimeInMilliseconds;

        public Bastard(Hero heroFather, Hero heroMother) {
            this.father = heroFather;
            this.mother = heroMother;

            double minYears = Bastards.Settings.MinimumYearsUntilBirth;
            double maxYears = Bastards.Settings.MaximumYearsUntilBirth;

            float yearsUntilBirth = (float)(minYears + (maxYears - minYears) * Bastards.Random.NextDouble());
            birthTimeInMilliseconds = CampaignTime.YearsFromNow(yearsUntilBirth).ToMilliseconds;

            BastardCampaignBehavior.Instance.Bastards.Add(this);
            heroMother.IsPregnant = true;

            if (Hero.MainHero == heroFather || Hero.MainHero == heroMother)
                Utils.PrintToMessages("{=BastardPlayerConceptionSuccess}{HERO_MOTHER_NAME} has gotten pregnant!", 255, 153, 204,
                    ("HERO_MOTHER_NAME", heroMother.Name.ToString()));
        }

        // Tick is done on daily hero tick, thrown in a Harmony patch on checking heroes for native pregnancy
        public void Tick() {
            // If bastard is unborn
            if (hero == null) {
                // Check if mother is dead
                if (!mother.IsAlive) {
                    mother.IsPregnant = false;
                    BastardCampaignBehavior.Instance.Bastards.Remove(this);
                    return;
                }

                // If it's time to pop
                if (CampaignTime.Now.ToMilliseconds >= birthTimeInMilliseconds)
                    Birth();
            }
        }

        public void Legitimize() {
            if (hero == null) return;

            // Set name without surname
            string moddedName = hero.Name.ToString();
            if (moddedName.Contains(" ")) {
                int indexOfSpace = moddedName.IndexOf(" ");
                moddedName = moddedName.Substring(0, indexOfSpace);
            }
            hero.SetName(new TextObject(moddedName), new TextObject(moddedName));

            // Remove from bastards list
            BastardCampaignBehavior.Instance.Bastards.Remove(this);
        }

        private void KeptSecret(Hero guardian) {
            if (hero == null) return;

            List<Hero> fatherChildren = (List<Hero>)AccessTools.Field(typeof(Hero), "_children").GetValue(hero.Father);
            fatherChildren.Remove(hero);
            hero.Father = guardian.Spouse;
            Legitimize();
        }

        private void SetBastardGuardian(Hero guardian, Hero? guardianSpouse = null, Hero? consequenceHero = null) {
            if (hero == null) return;

            Clan guardianClan = guardian.Clan;
            if (guardianClan != null) {
                hero.Clan = guardianClan;

                // See if caught or if child is considered legit
                if (guardianSpouse != null) {
                    // Secret only works because guardian is female, if the player is female sending a bastard the spouse obviously knows
                    if (guardian.IsFemale && Utils.PercentChanceCheck(Bastards.Settings.PercentChanceKeptSecret)) {
                        KeptSecret(guardian);
                        return;
                    }
                }

                if (consequenceHero != null)
                    DoConsequence(guardian, guardianSpouse, consequenceHero);
            }
            else {
                // Baby disappears, sold to orphanage or whatever
                Disappear();
            }
        }

        private void DoConsequence(Hero guardian, Hero? guardianSpouse, Hero consequenceHero) {
            if (guardian.Clan == Hero.MainHero.Clan) return;

            if (!Bastards.Settings.ConsequencesEnabled) return;

            Utils.ModifyHeroRelations(consequenceHero, guardianSpouse, Bastards.Settings.SpouseRelationLoss);

            if (guardian.Clan.Leader == guardian) return;

            Utils.ModifyHeroRelations(consequenceHero, guardian.Clan.Leader, Bastards.Settings.ClanLeaderRelationLoss);
        }

        public void Disappear() {
            if (hero == null)
                return;

            hero.Clan = null;
            hero.Father = null;
            hero.Mother = null;

            List<Hero> fatherChildren = (List<Hero>)AccessTools.Field(typeof(Hero), "_children").GetValue(father);
            List<Hero> motherChildren = (List<Hero>)AccessTools.Field(typeof(Hero), "_children").GetValue(mother);
            List<Hero> aliveHeroes = (List<Hero>)AccessTools.Field(typeof(CampaignObjectManager), "_aliveHeroes").GetValue(Campaign.Current.CampaignObjectManager);
            aliveHeroes.Remove(hero);
            fatherChildren.Remove(hero);
            motherChildren.Remove(hero);
            BastardCampaignBehavior.Instance.Bastards.Remove(this);
        }

        public void Birth() {
            // Set mother to not pregnant
            mother.IsPregnant = false;

            // Variables for campaign event
            List<Hero> aliveChildren = new();
            int stillbirthNum = 0;

            // Stillbirth chance
            if (Utils.PercentChanceCheck(Bastards.Settings.StillbirthChance)) {
                Utils.PrintToMessages("{=BastardBirthStillborn}{HERO_MOTHER_NAME} has delivered stillborn.", 255, 100, 100,
                    ("HERO_MOTHER_NAME", mother.Name.ToString()));
                BastardCampaignBehavior.Instance.Bastards.Remove(this);
                stillbirthNum++;
            } else {
                // Birth hero
                hero = HeroCreator.DeliverOffSpring(mother, father, Bastards.Random.Next(0, 2) >= 1 ? true : false);
                aliveChildren.Add(hero);
            }

            // Dispatch campaign event
            CampaignEventDispatcher.Instance.OnGivenBirth(mother, aliveChildren, stillbirthNum);

            // Grab spouse of mother hero for consequences as it sets to null on death/labor death
            Hero? spouseOfMother = mother.Spouse;
            // Mother dying in labor chance
            if (Utils.PercentChanceCheck(Bastards.Settings.LaborDeathChance)) {
                KillCharacterAction.ApplyInLabor(mother);
            }

            // If born when mother and father are married
            if (spouseOfMother == father) {
                Legitimize();
                return;
            }

            // If bastard was stillborn
            if (hero == null) return;

            // Set bastard as lord occupation
            hero.SetNewOccupation(Occupation.Lord);

            // Set GoT surnames
            if (Bastards.Settings.SurnamesEnabled) {
                string[] names = Utils.GetBastardName(hero);
                hero.SetName(new TextObject(names[1]), new TextObject(names[0]));
            }

            // Check if bastard is players child
            if (Hero.MainHero == father || Hero.MainHero == mother) {
                // Get other hero
                Hero otherHero = Hero.MainHero == father ? mother : father;

                if (otherHero.Clan != Hero.MainHero.Clan) {
                    Hero otherHeroSpouse = otherHero == mother ? spouseOfMother : otherHero.Spouse;

                    // Get clan inquiry text
                    TextObject textObject;
                    if (Hero.MainHero == mother)
                        textObject = new TextObject("{=BastardBirthPlayerIsMother}You have given birth to {BASTARDNAME}. Will you raise them as your own and take them into your clan?", null);
                    else {
                        textObject = new TextObject("{=BastardBirthPlayerIsFather}{BASTARDMOTHERNAME} has given birth to {BASTARDNAME}. Will you raise them as your own and take them into your clan?", null);
                        textObject.SetTextVariable("BASTARDMOTHERNAME", mother.Name);
                    }
                    textObject.SetTextVariable("BASTARDNAME", hero.FirstName);

                    // Perform clan inquiry
                    InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BastardBirthPlayerDisplayBoxTitle}Bastard Born", null).ToString(), textObject.ToString(), true, true, "Yes", "No",
                        // Yes, take them into my clan
                        () => {
                            SetBastardGuardian(Hero.MainHero);
                        },
                        // No, I will not take them into my clan
                        () => {
                            SetBastardGuardian(otherHero, otherHeroSpouse, Hero.MainHero);
                        },
                    ""), true);
                }
            }
            // If the bastard is not the current player character's child
            else {
                SetBastardGuardian(mother, spouseOfMother, father);
            }
        }
    }
}
