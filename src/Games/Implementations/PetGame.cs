using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PacManBot.Constants;
using PacManBot.Services;

namespace PacManBot.Games
{
    [DataContract]
    public class PetGame : BaseGame, IStoreableGame
    {
        public static readonly string[] FoodEmotes = new string[] { "🍌", "🍎", "🍊", "🍕", "🌮", "🍩", "🍪", "🍐", "🍉", "🍇", "🍑", "🍧", "🍫", "🥕", "🍼" };
        public static readonly string[] PlayEmotes = new string[] { "⚽", "🏀", "🏈", "🎾", "🏓", "🎨", "🎤", "🎭", "🏐", "🎣", };
        public static readonly string[] CleanEmotes = new string[] { "💧", "🚿", "🛁", "🚽", "🚰", "💦", "👣", "💩", "✨" };
        public static readonly string[] SleepEmotes = new string[] { "💤", "🛏", "🌃", "🌠", "🌙", "🌜" };
        public static readonly string[] BannerUrl = new string[] { null, "https://cdn.discordapp.com/attachments/412314001414815751/448939830433415189/copperbanner.png", "https://cdn.discordapp.com/attachments/412314001414815751/448939834354958370/silverbanner.png", "https://cdn.discordapp.com/attachments/412314001414815751/448939832102617090/goldbanner.png" };

        public const string PetAmountPattern = @"{-?[0-9]+}";
        public const int MaxStat = 20;

        public override string Name => "Clockagotchi";
        public override TimeSpan Expiry => TimeSpan.MaxValue;
        public string FilenameKey => "pet";

        public double TotalStats => satiation + happiness + hygiene + energy;

        [DataMember] private string petName = null;
        [DataMember] private string petImageUrl = null;
        [DataMember] public double satiation  = 15;
        [DataMember] public double happiness  = 15;
        [DataMember] public double hygiene  = 15;
        [DataMember] public double energy  = 15;
        [DataMember] public bool asleep  = false;
        [DataMember] public DateTime bornDate;
        [DataMember] public DateTime lastUpdated;
        [DataMember] public Achievements achievements = new Achievements();

        [DataMember] public override ulong OwnerId { get { return UserId[0]; } set { UserId = new ulong[] { value }; } }
        [IgnoreDataMember] public DateTime lastPet = DateTime.Now;

        public int TimesPet => achievements.timesPet;

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
        public class Achievements
        {
            [DataMember] public uint timesFed = 0;
            [DataMember] public uint timesPlayed = 0;
            [DataMember] public uint timesCleaned = 0;
            [DataMember] public int timesPet = 0;
            [DataMember] public DateTime lastNeglected = default;
            [DataMember] public uint Attention = 0;


            [Achievement("🎖", "At Home", "Give your pet a name and image", 1), DataMember]
            public bool Custom { get; set; } = false;

            [Achievement("🥉", "Good Care I", "20 Total actions", 5, group: 1)]
            public bool GoodCare1 => TotalActions >= 20;

            [Achievement("🥈", "Good Care II", "100 Total actions", 6, group: 1)]
            public bool GoodCare2 => TotalActions >= 100;

            [Achievement("🥇", "Good Care III", "300 Total actions", 7, group: 1)]
            public bool GoodCare3 => TotalActions >= 300;

            [Achievement("<:bronze:453367514550894602>", "Bronze Owner", "3 days without neglect", 10, hideIcon: true, group: 2)]
            public bool BronzeOwner => Attention >= 1;

            [Achievement("<:silver:453367514588774400>", "Silver Owner", "7 days without neglect", 11, hideIcon: true, group: 2)]
            public bool SilverOwner => Attention >= 2;

            [Achievement("<:gold:453368658303909888>", "Gold Owner", "14 days without neglect", 12, hideIcon: true, group: 2)]
            public bool GoldOwner => Attention >= 3;

            [Achievement("👑", "Pet King", "Be crowned king of pets", 100), DataMember]
            public bool PetKing { get; set; } = false;

            [Achievement("⭐", "Super Petting", "Pet 1,000 times", 101, group: 100), DataMember]
            public bool SuperPetting { get; set; } = false;

            [Achievement("👼", "Pet God", "Pet 10,000 times and be king", 102, group: 100), DataMember]
            public bool PetGod { get; set; } = false;


            public uint TotalActions => timesFed + timesPlayed + timesCleaned;


            public Achievements() { }


            public void DoChecks(PetGame pet)
            {
                if (lastNeglected == default) lastNeglected = pet.bornDate; //old pets
                else if (pet.TotalStats == 0) lastNeglected = DateTime.Now;
                var days = (DateTime.Now - lastNeglected).TotalDays;

                if (!string.IsNullOrWhiteSpace(pet.PetName) && !string.IsNullOrWhiteSpace(pet.PetImageUrl)) Custom = true;

                if (days >= 14 && Attention < 3) Attention = 3;
                else if (days >= 7 && Attention < 2) Attention = 2;
                else if (days >= 3 && Attention < 1) Attention = 1;
            }

            public List<AchievementAttribute> GetList()
            {
                return GetType().GetProperties()
                    .Select(x => x.GetCustomAttribute<AchievementAttribute>()?.WithObtained((bool)x.GetMethod.Invoke(this, new object[] { })))
                    .Where(x => x != null).ToList().Sorted();
            }

            public List<string> GetIcons(bool showHidden = false, bool highest = false)
            {
                var acs = GetList().Where(x => x.Obtained && (showHidden || !x.HideIcon)).ToList().Reversed();
                if (!highest) return acs.Select(x => x.Icon).ToList();

                var icons = new List<string>();
                var groups = new List<int>();
                foreach (var ac in acs)
                {
                    if (ac.Group == 0 || !groups.Contains(ac.Group))
                    {
                        icons.Add(ac.Icon);
                        groups.Add(ac.Group);
                    }
                }

                return icons;
            }
        }


        [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
        public class AchievementAttribute : Attribute, IComparable<AchievementAttribute>
        {
            public string Icon { get; private set; }
            public string Name { get; private set; }
            public string Description { get; private set; }
            public int Position { get; private set; }
            public int Group { get; private set; }
            public bool HideIcon { get; private set; }

            public bool Obtained { get; private set; }

            public AchievementAttribute(string icon, string name, string description, int position, bool hideIcon = false, int group = 0)
            {
                Icon = icon;
                Name = name;
                Description = description;
                Position = position;
                HideIcon = hideIcon;
                Group = group;
            }

            private AchievementAttribute(AchievementAttribute old, bool obtained) : this(old.Icon, old.Name, old.Description, old.Position, old.HideIcon, old.Group)
            {
                Obtained = obtained;
            }

            public AchievementAttribute WithObtained(bool obtained)
            {
                return new AchievementAttribute(this, obtained);
            }

            public int CompareTo(AchievementAttribute other)
            {
                return Position.CompareTo(other.Position);
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

            var unlocks = string.Join('\n', achievements.GetIcons().Split(3).Select(x => string.Join(' ', x)));

            return new EmbedBuilder
            {
                Title = $"{owner?.Nickname ?? Owner?.Username ?? "Unknown"}'s Clockagotchi",
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
                        Value = string.IsNullOrWhiteSpace(unlocks) ? "None" : unlocks,
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
            stats.Append($"**Pettings given:** {achievements.timesPet} (~{PetMessageCountEstimate()} messages)\n");
            stats.Append($"**Time without neglect:** {(DateTime.Now - achievements.lastNeglected).Humanized()}\n");
            stats.Append($"*(Neglect occurs when all meters reach 0)*\nᅠ");

            var achievs = new StringBuilder[] { new StringBuilder(), new StringBuilder() }; // off, on

            foreach (var ac in achievements.GetList())
            {
                achievs[ac.Obtained ? 1 : 0].Append($"\n{ac.Icon} **{ac.Name}** - {ac.Description}");
            }

            return new EmbedBuilder
            {
                Title = $"{owner?.Nickname ?? Owner?.Username ?? "Unknown"}'s Clockagotchi",
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

            achievements.DoChecks(this);
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
                energy = Math.Min(MaxStat, energy + 2);
                achievements.timesFed++;
            }
            else
            {
                happiness = Math.Max(0, happiness - 1);
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
            else if (energy.Ceiling() >= 5)
            {
                happiness = Math.Max(0, happiness - 1);
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
            else
            {
                happiness = Math.Max(0, happiness - 1);
            }

            storage.StoreGame(this);
            return canClean;
        }

        public void ToggleSleep()
        {
            asleep = !asleep;
            storage.StoreGame(this);
        }


        public string DoPet(SocketCommandContext context)
        {
            string pet;
            int amount;
            bool super = false;
            bool godEffect = false;

            do
            {
                if (achievements.SuperPetting && Bot.Random.OneIn(4))
                {
                    super = true;
                    pet = Bot.Random.Choose(storage.SuperPettingMessages);
                }
                else pet = Bot.Random.Choose(storage.PettingMessages);

                var match = Regex.Match(pet, PetAmountPattern);
                if (match.Success)
                {
                    amount = int.Parse(match.Value.Trim('{', '}'));
                    pet = pet.Replace(match.Value, "");
                }
                else amount = 1;

                if (achievements.PetGod && amount <= 0)
                {
                    godEffect = true;
                    amount = amount == 0 ? 100 : -10 * amount;
                }

            } while (pet.Contains("{king}") && !achievements.PetKing
                    || pet.Contains("{dm}") && context.Guild == null
                    || amount < 0 && (achievements.timesPet + amount < 0 || achievements.SuperPetting && achievements.timesPet + amount < 1000));


            achievements.timesPet += amount;
            bool king = pet.Contains("{king}");
            bool hide = pet.Contains("{hide}");

            if (!achievements.SuperPetting && achievements.timesPet >= 1000)
            {
                achievements.SuperPetting = true;
                pet += "\n\n⭐ **Congratulations!** You petted 1000 times and unlocked *Super Petting*.";
            }
            else if (!achievements.PetGod && achievements.PetKing && achievements.timesPet >= 10000)
            {
                achievements.PetGod = true;
                string name = (context.User as IGuildUser)?.Nickname ?? Owner?.Username ?? "???";
                pet = "👼 Having petted 10,000 times, and having lived a long and just life as Pet King, you and your pet ascend into the realm of the pet-angels.\n\n" +
                      $"After arriving to their heavenly dominion, some angels begin chanting: *\"{name.SanitizeMarkdown()}, {PetName}\"*. " +
                      $"Soon more and more join them, until ten billion voices act in unison. A blinding glare falls upon the pedestal you stand on. " +
                      "Your entire being slowly fades away, morphing into something else, something like... __pure petting energy__.\n" +
                      "The sounds of grand bells and trumpets fill the realm. You have been chosen as a new **Pet God**.\n\n" +
                      "Now, negative pets become positive and tenfold, and you get 100 pets each time you would have gotten 0. " +
                      "You return to the mortal world to resume your duties as king. You are also advised to finally stop petting so much.\n";
                amount = 0;
                hide = false;
                king = false;
                super = false;
            }
            else if (pet.Contains("{getking}"))
            {
                if (achievements.PetKing) pet = pet.Split("{getking}")[0] + " Again.";
                achievements.PetKing = true;
                king = true;
            }

            storage.StoreGame(this);

            pet = Regex.Replace(pet, @"{.*}", "");
            if ((amount == 0) == hide || godEffect) pet = pet.Trim(' ') + $" ({"👼 ".If(godEffect)}{amount.ToString("+0;-#")} pets)";
            if (super || king) pet = "⭐".If(super) + "👑".If(king) + " " + pet;

            return pet;
        }


        public virtual void PostDeserialize(DiscordShardedClient client, LoggingService logger, StorageService storage)
        {
            this.client = client;
            this.logger = logger;
            this.storage = storage;
        }


        public int PetMessageCountEstimate()
        {
            double sum = 0;

            sum += Math.Min(1000, achievements.timesPet) / AveragePets(storage, achievements.PetKing, false, false);
            if (achievements.timesPet > 1000) sum += Math.Min(9000, achievements.timesPet - 1000) / AveragePets(storage, achievements.PetKing, true, false);
            if (achievements.timesPet > 10000) sum += (achievements.timesPet - 10000) / AveragePets(storage, achievements.PetKing, true, true);

            return (int)sum;
        }


        public static double AveragePets(StorageService storage, bool king, bool super, bool god)
        {
            double sumNormal = 0;
            double sumSuper = 0;

            string[] allMessages = super ? Utils.ArrayConcat(storage.PettingMessages, storage.SuperPettingMessages) : storage.PettingMessages;
            int lines = allMessages.Length;
            int superIndex = storage.PettingMessages.Length;

            for (int i = 0; i < allMessages.Length; i++)
            {
                if (!king && allMessages[i].Contains("{king}")) lines--;
                else
                {
                    double pets;
                    var match = Regex.Match(allMessages[i], PetAmountPattern);
                    if (match.Success)
                    {
                        pets = int.Parse(match.Value.Trim('{', '}'));
                    }
                    else pets = 1;

                    if (pets <= 0 && god) pets = pets == 0 ? 100 : -10 * pets;

                    if (i >= superIndex) sumSuper += pets;
                    else sumNormal += pets;
                }
            }

            if (sumSuper > 0) sumNormal *= 0.75;
            sumSuper *= 0.25;

            return sumNormal / storage.PettingMessages.Length + sumSuper / storage.SuperPettingMessages.Length;
        }
    }
}