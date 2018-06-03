using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using PacManBot.Constants;
using PacManBot.Services;
using static PacManBot.Games.GameUtils;

namespace PacManBot.Games
{
    [DataContract]
    public class PetGame : BaseGame, ISingleplayerGame, IStoreableGame
    {
        public static readonly string[] FoodEmotes = new string[] { "🍌", "🍎", "🍊", "🍕", "🌮", "🍩", "🍪", "🍐", "🍉", "🍇", "🍑", "🍧", "🍫", "🥕", "🍼" };
        public static readonly string[] PlayEmotes = new string[] { "⚽", "🏀", "🏈", "🎾", "🏓", "🎨", "🎤", "🎭", "🏐", "🎣", };
        public static readonly string[] CleanEmotes = new string[] { "💧", "🚿", "🛁", "🚽", "🚰", "💦", "👣", "💩", "✨" };
        public static readonly string[] SleepEmotes = new string[] { "💤", "🛏", "🌃", "🌠", "⭐", "🌙", "🌜" };
        public static readonly string[] BannerUrl = new string[] { null, "https://cdn.discordapp.com/attachments/412314001414815751/448939830433415189/copperbanner.png", "https://cdn.discordapp.com/attachments/412314001414815751/448939834354958370/silverbanner.png", "https://cdn.discordapp.com/attachments/412314001414815751/448939832102617090/goldbanner.png" };

        public const string PetAmountPattern = @"{-?[0-9]+}";
        public const int MaxStat = 20;

        public override string Name => "Clockagotchi";
        public override TimeSpan Expiry => TimeSpan.MaxValue;
        public string FilenameKey => "pet";

        public double TotalStats => satiation + happiness + hygiene + energy;

        [DataMember] private string petName = null;
        [DataMember] private string petImageUrl = null;
        [DataMember] private double satiation = 15;
        [DataMember] private double happiness = 15;
        [DataMember] private double hygiene = 15;
        [DataMember] private double energy = 15;
        [DataMember] private bool asleep = false;
        [DataMember] private DateTime bornDate;
        [DataMember] private DateTime lastUpdated;
        [DataMember] private Achievements achievements = new Achievements();

        [DataMember] public ulong OwnerId { get { return UserId[0]; } set { UserId = new ulong[] { value }; } }
        [IgnoreDataMember] public DateTime lastPet = DateTime.Now;


        public double Satiation => satiation;
        public double Happiness => happiness;
        public double Hygiene => hygiene;
        public double Energy => energy;
        public bool Asleep => asleep;

        public string PetName
        {
            get { return petName; }
            set
            {
                petName = value?.SanitizeMarkdown().SanitizeMentions().Trim('<', '>');
                UpdateStats();
            }
        }

        public string PetImageUrl
        {
            get { return petImageUrl; }
            set
            {
                string url = value?.Trim('<', '>');
                if (url == null || Utils.IsImageUrl(url))
                {
                    petImageUrl = url;
                }
                else throw new FormatException();
                UpdateStats();
            }
        }


        [DataContract]
        private class Achievements
        {
            [DataMember] public uint timesFed = 0;
            [DataMember] public uint timesPlayed = 0;
            [DataMember] public uint timesCleaned = 0;
            [DataMember] public int timesPet = 0;
            [DataMember] public DateTime lastNeglected = default;

            [DataMember] public uint Attention = 0;
            [DataMember] public bool Custom = false;
            [DataMember] public bool SuperPetting = false;
            [DataMember] public bool PetKing = false;

            public bool GoodCare1 => TotalActions >= 20;
            public bool GoodCare2 => TotalActions >= 100;
            public bool GoodCare3 => TotalActions >= 500;

            public uint TotalActions => timesFed + timesPlayed + timesCleaned;


            public Achievements() { }


            public void Checks(PetGame pet)
            {
                if (lastNeglected == default) lastNeglected = pet.bornDate; //old pets
                else if (pet.TotalStats == 0) lastNeglected = DateTime.Now;
                var days = (DateTime.Now - lastNeglected).TotalDays;

                if (!string.IsNullOrWhiteSpace(pet.PetName) && !string.IsNullOrWhiteSpace(pet.PetImageUrl)) Custom = true;

                if (days >= 14 && Attention < 3) Attention = 3;
                else if (days >= 7 && Attention < 2) Attention = 2;
                else if (days >= 3 && Attention < 1) Attention = 1;
            }
        }




        private PetGame() { } // Used in serialization

        public PetGame(string name, ulong ownerId, DiscordShardedClient client, LoggingService logger, StorageService storage)
            : base(new ulong[] { ownerId }, client, logger, storage)
        {
            petName = name;
            bornDate = DateTime.Now;
            lastUpdated = DateTime.Now;
        }




        public override EmbedBuilder GetEmbed(bool showHelp = true) => GetEmbed(null);
        public EmbedBuilder GetEmbed(IGuildUser owner, bool decimals = false)
        {
            bool wasAsleep = asleep;
            UpdateStats();

            var description = new StringBuilder();

            string prefix = storage.GetPrefixOrEmpty(owner?.Guild);
            if (string.IsNullOrWhiteSpace(petName)) description.Append($"Congratulations on your new Clockagotchi!\nUse **{prefix}pet name** to name it and **{prefix}pet help** for more info\n\n");

            description.Append($"**Name:** {(string.IsNullOrWhiteSpace(petName) ? "*Unnamed*" : PetName)}\n");

            string age = (DateTime.Now - bornDate).Humanized();
            description.Append($"**Age:** {(age == "Just now" ? "Newborn" : age)}\nᅠ\n");

            if (TotalStats == 0) description.Append("❌ Oh no! Your pet is **Neglected**.\nHurry and make it feel better!\nᅠ");
            else if (TotalStats <= 5) description.Append("😱 Hurry! Your pet doesn't look very well!\nᅠ");


            var status = new StringBuilder();
            if (asleep) status.Append("💤💤💤\n\n");
            else if (wasAsleep) status.Append("Your pet woke up!\n\n");
            status.Append((satiation >= 5 ? "🍎" : "🍽") + $" **Satiation:** {(decimals ? satiation.ToString("0.000") : satiation.Ceiling().ToString())}/{MaxStat}\n");
            status.Append((happiness >= 5 ? "🏈" : "🕸") + $" **Happiness:** {(decimals ? happiness.ToString("0.000") : happiness.Ceiling().ToString())}/{MaxStat}\n");
            status.Append((hygiene >= 5 ? "🛁" : "💩")   + $" **Hygiene:** {(decimals ? hygiene.ToString("0.000") : hygiene.Ceiling().ToString())}/{MaxStat}\n");
            status.Append((energy >= 5 ? "⚡" : "🍂") + $" **Energy:** {(decimals ? energy.ToString("0.000") : energy.Ceiling().ToString())}/{MaxStat}\n");


            var unlocks = new StringBuilder();
            if (achievements.GoodCare3) unlocks.Append("🥇 ");
            if (achievements.GoodCare2) unlocks.Append("🥈 ");
            if (achievements.GoodCare1) unlocks.Append("🥉 ");
            if (achievements.Custom) unlocks.Append("🎖 ");
            if (achievements.SuperPetting) unlocks.Append("⭐ ");
            if (achievements.PetKing) unlocks.Append("👑 ");
            if (achievements.Attention > 0)
            {
                unlocks.Append('\n');
                unlocks.Append(achievements.Attention >= 3 ? "Gold" : achievements.Attention >= 2 ? "Silver" : "Bronze");
                unlocks.Append(" Banner");
            }


            return new EmbedBuilder
            {
                Title = $"{owner?.Nickname ?? client.GetUser(OwnerId).Username}'s Clockagotchi",
                Description = description.ToString(),
                Color = TotalStats.Ceiling() >= 60 ? new Color(0, 200, 0) : TotalStats.Ceiling() >= 25 ? new Color(255, 200, 0) : new Color(255, 0, 0),
                ThumbnailUrl = petImageUrl ?? "https://cdn.discordapp.com/attachments/353729197824278541/447979173554946051/clockagotchi.png",
                ImageUrl = BannerUrl[achievements.Attention],
                Fields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder()
                    {
                        IsInline = true,
                        Name = "Status",
                        Value = status.ToString(),
                    },
                    new EmbedFieldBuilder()
                    {
                        IsInline = true,
                        Name = "Unlocks",
                        Value = unlocks.Length == 0 ? "*None*" : unlocks.ToString(),
                    },
                }
            };
        }


        public EmbedBuilder GetEmbedAchievements(IGuildUser owner)
        {
            UpdateStats();

            var stats = new StringBuilder();
            stats.Append($"**Times fed:** {achievements.timesFed}\n");
            stats.Append($"**Times played:** {achievements.timesPlayed}\n");
            stats.Append($"**Times cleaned:** {achievements.timesCleaned}\n");
            stats.Append($"**Total actions:** {achievements.TotalActions}\n");
            stats.Append($"**Pettings given:** {achievements.timesPet}\n");
            stats.Append($"**Time without neglect:** {(DateTime.Now - achievements.lastNeglected).Humanized()}\n");
            stats.Append($"*(Neglect occurs when all stats reach 0)*\nᅠ");

            var achievs = new StringBuilder[] { new StringBuilder(), new StringBuilder() }; // off, on
            achievs[achievements.Custom ? 1 : 0].Append("\n🎖 **At Home** - Give your pet a name and image");
            achievs[achievements.GoodCare1 ? 1 : 0].Append("\n🥉 **Good Care I** - 20 Total actions");
            achievs[achievements.GoodCare2 ? 1 : 0].Append("\n🥈 **Good Care II** - 100 Total actions");
            achievs[achievements.GoodCare3 ? 1 : 0].Append("\n🥇 **Good Care III** - 500 Total actions");
            achievs[achievements.PetKing ? 1 : 0].Append("\n👑 **Pet King** - Be crowned king of pets");
            achievs[achievements.SuperPetting ? 1 : 0].Append("\n⭐ **Super Petting** - Pet 1000 times");
            achievs[achievements.Attention >= 1 ? 1 : 0].Append("\n**Bronze Banner** - 3 days without neglect");
            achievs[achievements.Attention >= 2 ? 1 : 0].Append("\n**Silver Banner** - 7 days without neglect");
            achievs[achievements.Attention >= 3 ? 1 : 0].Append("\n**Gold Banner** - 14 days without neglect");

            return new EmbedBuilder
            {
                Title = $"{owner?.Nickname ?? client.GetUser(OwnerId).Username}'s Clockagotchi",
                Color = new Color(150, 0, 220),
                ThumbnailUrl = petImageUrl ?? "https://cdn.discordapp.com/attachments/353729197824278541/447979173554946051/clockagotchi.png",
                Fields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder()
                    {
                        IsInline = false,
                        Name = "Statistics 📊",
                        Value = stats.ToString(),
                    },
                    new EmbedFieldBuilder()
                    {
                        IsInline = false,
                        Name = "Achievements 🏆",
                        Value = achievs[1].ToString().Replace("\n", $"\n{CustomEmoji.Check}") + achievs[0].ToString(),
                    },
                }
            };
        }


        public void UpdateStats(bool store = true)
        {
            var now = DateTime.Now;
            double hours = (now - lastUpdated).TotalHours;

            satiation = Math.Max(0, satiation - hours * Bot.Random.NextDouble(0.75, 1.25));
            happiness = Math.Max(0, happiness - hours * 1.1 * Bot.Random.NextDouble(0.75, 1.25));
            hygiene = Math.Max(0, hygiene - hours * 0.7 * Bot.Random.NextDouble(0.75, 1.25));

            double asleepMult = 4 * Bot.Random.NextDouble(0.75, 1.25);
            double awakeMult = -1.2 * Bot.Random.NextDouble(0.75, 1.25);
            energy = Math.Max(0, energy + hours * (asleep ? asleepMult : awakeMult));
            if (asleep && energy >= MaxStat) // Wakes up
            {
                double excessHours = (energy - MaxStat) / asleepMult;
                energy = Math.Max(0, MaxStat + excessHours * awakeMult);
                asleep = false;
            }

            achievements.Checks(this);
            lastUpdated = now;
            if (store) storage.StoreGame(this);
        }


        public bool Feed()
        {
            UpdateStats(store: false);

            bool canEat = satiation.Ceiling() != MaxStat;
            if (canEat)
            {
                satiation = MaxStat;
                energy = Math.Min(MaxStat, energy + 1);
                achievements.timesFed++;
            }
            storage.StoreGame(this);
            return canEat;
        }

        public bool Play()
        {
            UpdateStats(store: false);

            bool canPlay = happiness.Ceiling() != MaxStat && energy.Ceiling() >= 5;
            if (canPlay)
            {
                happiness = MaxStat;
                energy = Math.Max(0, energy - (energy.Ceiling() == MaxStat ? 5.5 : 5.0)); // It's all for appearance
                achievements.timesPlayed++;
            }
            storage.StoreGame(this);
            return canPlay;
        }

        public bool Clean()
        {
            UpdateStats(store: false);

            bool canClean = hygiene.Ceiling() != MaxStat;
            if (canClean)
            {
                hygiene = MaxStat;
                achievements.timesCleaned++;
            }
            storage.StoreGame(this);
            return canClean;
        }

        public void ToggleSleep()
        {
            UpdateStats(store: false);
            asleep = !asleep;
            storage.StoreGame(this);
        }


        public string Pet()
        {
            string pet;

            if (achievements.SuperPetting && Bot.Random.OneIn(5)) pet = $"⭐ {Bot.Random.Choose(storage.SuperPettingMessages)}";
            else pet = Bot.Random.Choose(storage.PettingMessages);

            var match = Regex.Match(pet, PetAmountPattern);
            if (match.Success)
            {
                achievements.timesPet += int.Parse(match.Value.Trim('{', '}'));
                pet = pet.Replace(match.Value, "");
            }
            else achievements.timesPet += 1;

            if (pet.Contains("{king}"))
            {
                pet = "👑 ".If(!achievements.PetKing) + pet.Replace("{king}", "");
                achievements.PetKing = true;
            }

            if (achievements.timesPet >= 1000 && !achievements.SuperPetting)
            {
                achievements.SuperPetting = true;
                achievements.PetKing = true;
                pet += "\n\n⭐ **Congratulations!** You petted 1000 times and unlocked *Super Petting*.";
            }

            storage.StoreGame(this);

            return pet;
        }


        public virtual void SetServices(DiscordShardedClient client, LoggingService logger, StorageService storage)
        {
            this.client = client;
            this.logger = logger;
            this.storage = storage;
        }
    }
}