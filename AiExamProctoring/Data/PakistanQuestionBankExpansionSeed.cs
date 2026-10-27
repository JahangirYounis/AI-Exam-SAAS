using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Data;

/// <summary>
/// Expands the starter bank to the broad subject coverage used in Pakistani
/// academic, admission and recruitment tests. Each subject receives 52
/// verified starter MCQs (13 knowledge points × four independently worded
/// test items). Teachers may add their own organisation-specific questions.
/// </summary>
public static class PakistanQuestionBankExpansionSeed
{
    private sealed record Fact(string Stem, string Answer, string Wrong1, string Wrong2, string Wrong3, string Topic, string Explanation);
    private sealed record SubjectFacts(string Name, string Description, Fact[] Facts);

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        foreach (var source in BuildSubjects())
        {
            var subject = await context.Subjects.FirstOrDefaultAsync(x => x.SubjectName == source.Name);
            if (subject is null)
            {
                subject = new Subject
                {
                    SubjectName = source.Name,
                    Description = source.Description,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                };
                context.Subjects.Add(subject);
                await context.SaveChangesAsync();
            }
            else if (!subject.IsActive)
            {
                subject.IsActive = true;
                subject.UpdatedDate = DateTime.Now;
                await context.SaveChangesAsync();
            }

            var existing = (await context.Questions
                    .Where(x => x.SubjectId == subject.SubjectId)
                    .Select(x => x.QuestionText)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nextOrder = await context.Questions
                .Where(x => x.SubjectId == subject.SubjectId)
                .Select(x => (int?)x.DisplayOrder)
                .MaxAsync() ?? 0;
            var additions = new List<Question>();

            foreach (var question in Expand(source.Facts))
            {
                if (!existing.Add(question.Text)) continue;
                additions.Add(new Question
                {
                    SubjectId = subject.SubjectId,
                    QuestionText = question.Text,
                    OptionA = question.A,
                    OptionB = question.B,
                    OptionC = question.C,
                    OptionD = question.D,
                    CorrectAnswer = question.Correct,
                    Topic = question.Topic,
                    Tags = $"Pakistan Test Bank,Starter,{source.Name}",
                    Explanation = question.Explanation,
                    DisplayOrder = ++nextOrder,
                    IsActive = true,
                    IsVerified = true,
                    CreatedBy = "System Seed",
                    CreatedDate = DateTime.Now
                });
            }

            if (additions.Count == 0) continue;
            context.Questions.AddRange(additions);
            await context.SaveChangesAsync();
            logger.LogInformation("Expanded {Subject} with {Count} verified starter MCQs.", source.Name, additions.Count);
        }
    }

    private sealed record ExpandedQuestion(string Text, string A, string B, string C, string D, string Correct, string Topic, string Explanation);

    private static IEnumerable<ExpandedQuestion> Expand(IEnumerable<Fact> facts)
    {
        foreach (var fact in facts)
        {
            yield return new($"What is the {fact.Stem}?", fact.Answer, fact.Wrong1, fact.Wrong2, fact.Wrong3, "A", fact.Topic, fact.Explanation);
            yield return new($"Choose the correct answer for the {fact.Stem}.", fact.Wrong1, fact.Answer, fact.Wrong2, fact.Wrong3, "B", fact.Topic, fact.Explanation);
            yield return new($"Which option correctly identifies the {fact.Stem}?", fact.Wrong2, fact.Wrong3, fact.Answer, fact.Wrong1, "C", fact.Topic, fact.Explanation);
            yield return new($"In a standard objective test, select the correct {fact.Stem}.", fact.Wrong3, fact.Wrong2, fact.Wrong1, fact.Answer, "D", fact.Topic, fact.Explanation);
        }
    }

    private static SubjectFacts[] BuildSubjects() =>
    [
        S("Physics", "Mechanics, waves, electricity, optics and modern physics for board, admission and job tests",
            F("SI unit of force", "newton", "joule", "watt", "pascal", "Mechanics", "Force is measured in newtons."),
            F("formula for speed", "distance ÷ time", "time ÷ distance", "mass × acceleration", "work ÷ distance", "Mechanics", "Average speed equals distance divided by time."),
            F("unit of electric current", "ampere", "volt", "ohm", "coulomb", "Electricity", "Electric current is measured in amperes."),
            F("value of gravitational acceleration near Earth", "9.8 m/s²", "3.0 m/s²", "98 m/s²", "0.98 m/s²", "Mechanics", "Standard gravity is approximately 9.8 m/s²."),
            F("speed of light in vacuum", "3 × 10⁸ m/s", "3 × 10⁶ m/s", "3 × 10⁴ m/s", "3 × 10¹⁰ m/s", "Optics", "Light travels at approximately 3 × 10⁸ m/s in vacuum."),
            F("unit of frequency", "hertz", "tesla", "weber", "decibel", "Waves", "Frequency is measured in hertz."),
            F("mirror used for a wide rear view", "convex mirror", "concave mirror", "plane mirror only", "parabolic mirror", "Optics", "A convex mirror gives a wider field of view."),
            F("charge carried by an electron", "negative", "positive", "neutral", "variable positive", "Modern Physics", "An electron carries negative charge."),
            F("formula for electrical power", "P = VI", "P = V/I", "P = I/V", "P = Q/t²", "Electricity", "Electrical power equals voltage multiplied by current."),
            F("unit of resistance", "ohm", "ampere", "volt", "farad", "Electricity", "Resistance is measured in ohms."),
            F("mode of heat transfer through direct contact", "conduction", "convection", "radiation", "reflection", "Thermal Physics", "Direct molecular contact transfers heat by conduction."),
            F("particle found in the nucleus with no charge", "neutron", "electron", "positron", "photon", "Modern Physics", "A neutron is electrically neutral and lies in the nucleus."),
            F("energy of a moving object", "kinetic energy", "potential energy", "chemical energy", "nuclear binding only", "Energy", "Motion gives an object kinetic energy.")),

        S("Chemistry", "Atomic structure, bonding, reactions, acids, bases and organic chemistry",
            F("atomic number of hydrogen", "1", "2", "6", "8", "Atomic Structure", "Hydrogen has one proton and atomic number 1."),
            F("symbol of sodium", "Na", "S", "So", "N", "Periodic Table", "The chemical symbol of sodium is Na."),
            F("pH of a neutral solution at 25°C", "7", "0", "5", "14", "Acids and Bases", "A neutral solution has pH 7 at 25°C."),
            F("gas released by acid and carbonate", "carbon dioxide", "oxygen", "nitrogen", "chlorine", "Reactions", "Acids reacting with carbonates release carbon dioxide."),
            F("bond formed by electron transfer", "ionic bond", "covalent bond", "metallic bond", "hydrogen bond", "Bonding", "Ionic bonds form after electron transfer."),
            F("bond formed by sharing electrons", "covalent bond", "ionic bond", "metallic bond", "electrostatic ion bond only", "Bonding", "Covalent bonding involves shared electrons."),
            F("most abundant gas in Earth's atmosphere", "nitrogen", "oxygen", "carbon dioxide", "hydrogen", "Environmental Chemistry", "Nitrogen makes up about 78 percent of the atmosphere."),
            F("Avogadro constant", "6.022 × 10²³", "9.8 × 10²", "3 × 10⁸", "1.6 × 10⁻¹⁹", "Mole Concept", "One mole contains about 6.022 × 10²³ entities."),
            F("common strong acid among the options", "hydrochloric acid", "acetic acid", "water", "ammonia", "Acids and Bases", "Hydrochloric acid is a strong acid in water."),
            F("meaning of oxidation in electron terms", "loss of electrons", "gain of electrons", "gain of neutrons", "loss of all protons", "Redox", "Oxidation is loss of electrons."),
            F("functional group represented by -OH", "hydroxyl", "carbonyl", "amino", "carboxyl", "Organic Chemistry", "-OH is the hydroxyl functional group."),
            F("simplest alkane", "methane", "ethene", "ethyne", "benzene", "Organic Chemistry", "Methane, CH₄, is the simplest alkane."),
            F("method used to recover crystals from a solution", "crystallization", "filtration only", "decantation", "chromatography only", "Separation", "Crystallization forms solid crystals from a solution.")),

        S("Biology", "Cells, genetics, human physiology, plants, ecology and microorganisms",
            F("basic unit of life", "cell", "tissue", "organ", "organ system", "Cell Biology", "The cell is the basic structural and functional unit of life."),
            F("powerhouse of the cell", "mitochondrion", "nucleus", "ribosome", "Golgi body", "Cell Biology", "Mitochondria produce much of the cell's ATP."),
            F("full form of DNA", "deoxyribonucleic acid", "double nitrogen acid", "deoxygenated nucleic atom", "dynamic nuclear acid", "Genetics", "DNA stands for deoxyribonucleic acid."),
            F("site of photosynthesis", "chloroplast", "mitochondrion", "nucleus", "lysosome", "Plant Biology", "Photosynthesis mainly occurs in chloroplasts."),
            F("green photosynthetic pigment", "chlorophyll", "haemoglobin", "melanin", "keratin", "Plant Biology", "Chlorophyll absorbs light for photosynthesis."),
            F("number of chambers in the human heart", "four", "two", "three", "five", "Human Biology", "The human heart has two atria and two ventricles."),
            F("primary function of red blood cells", "transport oxygen", "produce bile", "digest protein", "form urine", "Human Biology", "Haemoglobin in red cells carries oxygen."),
            F("organ that filters blood and forms urine", "kidney", "heart", "stomach", "pancreas", "Human Biology", "The kidneys filter blood and produce urine."),
            F("gland that produces insulin", "pancreas", "thyroid", "pituitary", "adrenal only", "Human Biology", "Pancreatic beta cells produce insulin."),
            F("process of cell division for growth", "mitosis", "meiosis", "osmosis", "diffusion", "Cell Biology", "Mitosis produces genetically similar body cells."),
            F("movement of water through a selectively permeable membrane", "osmosis", "respiration", "transpiration only", "active transport of all solutes", "Cell Transport", "Osmosis is movement of water across a selectively permeable membrane."),
            F("gas released during photosynthesis", "oxygen", "carbon dioxide", "nitrogen", "methane", "Plant Biology", "Oxygen is released as a product of photosynthesis."),
            F("largest organ of the human body", "skin", "liver", "brain", "lung", "Human Biology", "Skin is the body's largest organ.")),

        S("English", "Grammar, vocabulary, comprehension and sentence correction",
            F("past tense of go", "went", "goed", "gone", "going", "Tenses", "The simple past of go is went."),
            F("plural of child", "children", "childs", "childes", "childrens", "Grammar", "Children is the irregular plural of child."),
            F("synonym of rapid", "fast", "slow", "weak", "late", "Vocabulary", "Rapid means fast or quick."),
            F("antonym of ancient", "modern", "old", "historic", "former", "Vocabulary", "Modern is an antonym of ancient."),
            F("correct article before honest", "an", "a", "the only", "no article in every case", "Articles", "Honest begins with a vowel sound, so it takes an."),
            F("part of speech of quickly", "adverb", "noun", "pronoun", "preposition", "Parts of Speech", "Quickly modifies a verb and is an adverb."),
            F("comparative form of good", "better", "gooder", "best", "more good", "Comparison", "Better is the irregular comparative of good."),
            F("correct spelling", "necessary", "neccessary", "necessery", "necesary", "Spelling", "Necessary is spelled with one c and two s letters."),
            F("active voice form of The book was read by Ali", "Ali read the book", "Ali was read by the book", "The book read Ali", "Ali is reading by book", "Voice", "The agent Ali becomes the subject in active voice."),
            F("preposition in interested in science", "in", "on", "at", "for", "Prepositions", "The standard collocation is interested in."),
            F("meaning of concise", "brief but clear", "very confusing", "extremely lengthy", "careless", "Vocabulary", "Concise writing is brief and clear."),
            F("type of sentence Please close the door", "imperative", "interrogative", "exclamatory only", "declarative", "Sentence Types", "An imperative sentence gives a command or request."),
            F("subject in The students completed the test", "the students", "completed", "the test", "test completed", "Grammar", "The students perform the action and form the subject.")),

        S("General Science", "Broad science awareness for entry and recruitment tests",
            F("planet known as the Red Planet", "Mars", "Venus", "Jupiter", "Mercury", "Astronomy", "Iron oxides give Mars its reddish appearance."),
            F("largest planet in the Solar System", "Jupiter", "Earth", "Saturn", "Mars", "Astronomy", "Jupiter is the largest planet."),
            F("gas essential for human respiration", "oxygen", "nitrogen", "carbon dioxide", "helium", "Health Science", "Humans use oxygen in cellular respiration."),
            F("boiling point of water at sea level", "100°C", "0°C", "50°C", "150°C", "Physical Science", "Pure water boils at 100°C at standard pressure."),
            F("freezing point of water", "0°C", "10°C", "32°C above zero", "100°C", "Physical Science", "Water freezes at 0°C under standard conditions."),
            F("closest star to Earth", "the Sun", "Sirius", "Polaris", "Alpha Centauri A", "Astronomy", "The Sun is Earth's nearest star."),
            F("vitamin mainly made in skin using sunlight", "vitamin D", "vitamin C", "vitamin B12", "vitamin K only", "Health", "Sunlight supports vitamin D synthesis in skin."),
            F("hardest natural substance", "diamond", "gold", "iron", "quartz", "Earth Science", "Diamond is the hardest common natural material."),
            F("instrument used to measure temperature", "thermometer", "barometer", "ammeter", "hygrometer", "Measurement", "A thermometer measures temperature."),
            F("instrument used to measure atmospheric pressure", "barometer", "thermometer", "voltmeter", "seismograph", "Measurement", "A barometer measures atmospheric pressure."),
            F("main source of energy for Earth", "the Sun", "the Moon", "Earth's core only", "ocean tides only", "Environment", "Most energy driving Earth's systems comes from the Sun."),
            F("process by which liquid changes to gas", "evaporation", "condensation", "freezing", "sublimation of solid only", "Matter", "Evaporation changes a liquid into vapour."),
            F("layer that protects Earth from much ultraviolet radiation", "ozone layer", "troposphere only", "ionosphere only", "Earth's crust", "Environment", "Stratospheric ozone absorbs much harmful ultraviolet radiation.")),

        S("Current Affairs", "Contemporary awareness, major institutions and international organisations; teachers should refresh time-sensitive items",
            F("headquarters city of the United Nations", "New York", "Geneva", "Paris", "Vienna", "International Organisations", "The main UN headquarters is in New York City."),
            F("headquarters city of the World Health Organization", "Geneva", "New York", "Rome", "Brussels", "International Organisations", "WHO is headquartered in Geneva."),
            F("currency used by most European Union euro-area states", "euro", "pound sterling", "dollar", "franc", "World Affairs", "The euro is the shared currency of the euro area."),
            F("international body responsible for global civil aviation standards", "ICAO", "UNESCO", "ILO", "WTO", "International Organisations", "ICAO develops international civil aviation standards."),
            F("organisation focused on global trade rules", "WTO", "WHO", "UNICEF", "IAEA", "International Organisations", "The World Trade Organization deals with rules of trade between nations."),
            F("UN agency focused on children", "UNICEF", "UNDP", "FAO", "ICAO", "International Organisations", "UNICEF works for children's rights and wellbeing."),
            F("international court located in The Hague", "International Court of Justice", "World Bank", "UN General Assembly", "UNESCO", "International Law", "The International Court of Justice sits in The Hague."),
            F("organisation that publishes the World Economic Outlook", "International Monetary Fund", "UNESCO", "Interpol", "NATO", "Economy", "The IMF publishes the World Economic Outlook."),
            F("UN specialised agency for education, science and culture", "UNESCO", "WHO", "WTO", "ICAO", "International Organisations", "UNESCO covers education, science and culture."),
            F("international day observed on 8 March", "International Women's Day", "World Health Day", "World Environment Day", "Human Rights Day", "International Days", "International Women's Day is observed on 8 March."),
            F("World Environment Day date", "5 June", "22 April", "10 December", "24 October", "International Days", "World Environment Day is observed on 5 June."),
            F("UN Day date", "24 October", "14 August", "23 March", "1 May", "International Days", "United Nations Day is observed on 24 October."),
            F("global development framework ending in 2030", "Sustainable Development Goals", "Millennium Charter only", "Kyoto currency system", "Bretton Woods Goals", "Development", "The UN Sustainable Development Goals have a 2030 horizon.")),

        S("Pakistan Studies", "History, geography, constitution, culture and national institutions of Pakistan",
            F("independence date of Pakistan", "14 August 1947", "23 March 1940", "15 August 1948", "11 September 1948", "History", "Pakistan became independent on 14 August 1947."),
            F("year of the Lahore Resolution", "1940", "1930", "1947", "1956", "History", "The Lahore Resolution was adopted in 1940."),
            F("first Governor-General of Pakistan", "Muhammad Ali Jinnah", "Liaquat Ali Khan", "Khawaja Nazimuddin", "Iskander Mirza", "History", "Muhammad Ali Jinnah was the first Governor-General."),
            F("first Prime Minister of Pakistan", "Liaquat Ali Khan", "Muhammad Ali Jinnah", "Ayub Khan", "Zulfikar Ali Bhutto", "History", "Liaquat Ali Khan was Pakistan's first Prime Minister."),
            F("year of Pakistan's current constitutional framework", "1973", "1956", "1962", "1985", "Constitution", "The current Constitution was originally enacted in 1973."),
            F("national language of Pakistan", "Urdu", "Punjabi", "Sindhi", "English", "Culture", "Urdu is the national language under the Constitution."),
            F("federal capital of Pakistan", "Islamabad", "Karachi", "Lahore", "Rawalpindi", "Geography", "Islamabad is the federal capital."),
            F("largest province of Pakistan by area", "Balochistan", "Punjab", "Sindh", "Khyber Pakhtunkhwa", "Geography", "Balochistan is the largest province by land area."),
            F("highest mountain in Pakistan", "K2", "Nanga Parbat", "Tirich Mir", "Rakaposhi", "Geography", "K2 is Pakistan's highest mountain."),
            F("national flower of Pakistan", "jasmine", "rose", "tulip", "sunflower", "Culture", "Jasmine is Pakistan's national flower."),
            F("national animal of Pakistan", "markhor", "lion", "camel", "snow leopard", "Culture", "The markhor is Pakistan's national animal."),
            F("first capital of Pakistan", "Karachi", "Lahore", "Islamabad", "Peshawar", "History", "Karachi was Pakistan's first capital."),
            F("city where Minar-e-Pakistan is located", "Lahore", "Karachi", "Islamabad", "Multan", "History", "Minar-e-Pakistan is in Lahore.")),

        S("Computer Science", "Computer fundamentals, programming, databases, networks and cyber security",
            F("main processing unit of a computer", "CPU", "RAM", "monitor", "keyboard", "Fundamentals", "The CPU executes program instructions."),
            F("volatile working memory", "RAM", "ROM", "SSD", "DVD", "Fundamentals", "RAM loses its contents when power is removed."),
            F("number system using only 0 and 1", "binary", "decimal", "octal", "hexadecimal", "Number Systems", "Binary uses two digits: 0 and 1."),
            F("language used to structure web pages", "HTML", "CSS", "SQL", "Python only", "Web", "HTML defines web page structure."),
            F("technology used to style web pages", "CSS", "HTML", "SQL", "DNS", "Web", "CSS controls web presentation."),
            F("language commonly used for relational database queries", "SQL", "HTML", "CSS", "SMTP", "Databases", "SQL queries and manages relational data."),
            F("data structure following LIFO", "stack", "queue", "graph", "linked list only", "Data Structures", "A stack is Last-In, First-Out."),
            F("data structure following FIFO", "queue", "stack", "heap", "tree", "Data Structures", "A queue is First-In, First-Out."),
            F("requirement for binary search", "sorted data", "encrypted data", "random unsorted data", "a database only", "Algorithms", "Binary search requires ordered data."),
            F("secure web transfer protocol", "HTTPS", "HTTP without TLS", "FTP", "SMTP", "Networking", "HTTPS protects HTTP traffic using TLS."),
            F("service translating domain names to IP addresses", "DNS", "DHCP", "SMTP", "HTML", "Networking", "DNS resolves domain names."),
            F("device or software that filters network traffic", "firewall", "compiler", "spreadsheet", "scanner", "Security", "A firewall applies network traffic rules."),
            F("HTTP method normally used to retrieve a resource", "GET", "DELETE", "PATCH", "TRACE only", "Web", "GET retrieves a resource representation.")),

        S("Mathematics", "Arithmetic, algebra, geometry, trigonometry and basic calculus",
            F("result of 18 + 27", "45", "35", "44", "55", "Arithmetic", "18 + 27 = 45."),
            F("result of 12 × 9", "108", "96", "118", "21", "Arithmetic", "12 multiplied by 9 equals 108."),
            F("square root of 144", "12", "10", "14", "16", "Arithmetic", "12 × 12 = 144."),
            F("value of x when x + 9 = 21", "12", "10", "11", "13", "Algebra", "Subtracting 9 gives x = 12."),
            F("value of x when 3x = 27", "9", "6", "8", "12", "Algebra", "Dividing by 3 gives x = 9."),
            F("25 percent of 240", "60", "40", "50", "80", "Percentages", "One quarter of 240 is 60."),
            F("perimeter of a square with side 6 cm", "24 cm", "12 cm", "18 cm", "36 cm", "Geometry", "Perimeter is four times the side."),
            F("area of a 9 cm by 4 cm rectangle", "36 cm²", "13 cm²", "26 cm²", "45 cm²", "Geometry", "Area equals length multiplied by width."),
            F("sum of interior angles of a triangle", "180°", "90°", "270°", "360°", "Geometry", "Triangle angles sum to 180 degrees."),
            F("value of sin 90°", "1", "0", "-1", "1/2", "Trigonometry", "Sine of 90 degrees is 1."),
            F("mean of 4, 6 and 8", "6", "5", "7", "8", "Statistics", "(4 + 6 + 8) ÷ 3 = 6."),
            F("probability of a head on one fair coin toss", "1/2", "1/4", "0", "1", "Probability", "One of two equally likely outcomes is a head."),
            F("derivative of x²", "2x", "x", "x²", "2", "Calculus", "The power rule gives 2x.")),

        S("Islamiat", "Foundational Islamic studies commonly included in Pakistani tests",
            F("holy book revealed to Prophet Muhammad (PBUH)", "the Quran", "the Torah", "the Psalms", "the Gospel", "Quran", "The Quran was revealed to Prophet Muhammad (PBUH)."),
            F("number of pillars of Islam", "five", "three", "six", "seven", "Beliefs and Practice", "Islam has five pillars."),
            F("first pillar of Islam", "Shahadah", "Zakat", "Hajj", "Sawm", "Beliefs and Practice", "The declaration of faith is the first pillar."),
            F("obligatory daily prayers", "five", "two", "three", "seven", "Salah", "Muslims offer five obligatory prayers each day."),
            F("month of obligatory fasting", "Ramadan", "Muharram", "Rajab", "Shawwal", "Fasting", "Obligatory fasting is observed in Ramadan."),
            F("city of the Kaaba", "Makkah", "Madinah", "Jerusalem", "Taif", "Islamic Places", "The Kaaba is in Makkah."),
            F("first month of the Islamic calendar", "Muharram", "Ramadan", "Safar", "Dhul-Hijjah", "Calendar", "Muharram is the first Hijri month."),
            F("last month of the Islamic calendar", "Dhul-Hijjah", "Ramadan", "Shawwal", "Rajab", "Calendar", "Dhul-Hijjah is the twelfth Hijri month."),
            F("direction faced during prayer", "Qiblah", "Mina", "Arafat", "Miqat", "Salah", "Muslims face the Qiblah during prayer."),
            F("obligatory charity pillar", "Zakat", "Sawm", "Hajj", "Adhan", "Charity", "Zakat is obligatory charity subject to its conditions."),
            F("pilgrimage pillar performed in Makkah", "Hajj", "Umrah only", "I'tikaf", "Hijrah", "Hajj", "Hajj is the obligatory pilgrimage for those able to perform it."),
            F("migration from Makkah to Madinah", "Hijrah", "Isra", "Mi'raj", "Bay'ah", "Seerah", "Hijrah refers to the migration to Madinah."),
            F("first revealed word traditionally identified in Surah Al-Alaq", "Iqra", "Qul", "Alhamdu", "Subhan", "Quran", "The first revealed command is traditionally identified as Iqra, meaning read/recite.")),

        S("Urdu", "Urdu grammar, vocabulary, idioms and literary basics",
            F("Urdu term for noun", "اسم", "فعل", "حرف", "صفت", "قواعد", "اسم کسی شخص، جگہ یا چیز کے نام کو کہتے ہیں۔"),
            F("Urdu term for verb", "فعل", "اسم", "ضمیر", "حرف", "قواعد", "فعل کام یا حالت ظاہر کرتا ہے۔"),
            F("plural of کتاب", "کتابیں", "کتابان", "کتابون", "کتابی", "واحد جمع", "کتاب کی معروف جمع کتابیں ہے۔"),
            F("opposite of خوش", "اداس", "شاد", "مسرور", "خندان", "متضاد", "خوش کا متضاد اداس ہے۔"),
            F("synonym of آسمان", "فلک", "زمین", "دریا", "صحرا", "مترادف", "فلک آسمان کا مترادف ہے۔"),
            F("meaning of محنت", "کوشش", "آرام", "غفلت", "خاموشی", "الفاظ", "محنت کا مطلب کوشش اور جدوجہد ہے۔"),
            F("punctuation mark used at the end of a question", "علامتِ سوال", "وقفہ کامل", "قوسین", "واوین", "رموز اوقاف", "سوالیہ جملے کے آخر میں علامت سوال آتی ہے۔"),
            F("writer of the national anthem lyrics", "حفیظ جالندھری", "علامہ اقبال", "فیض احمد فیض", "احمد فراز", "ادب", "پاکستان کے قومی ترانے کے الفاظ حفیظ جالندھری نے لکھے۔"),
            F("national poet of Pakistan", "علامہ محمد اقبال", "مرزا غالب", "میر تقی میر", "احمد فراز", "ادب", "علامہ اقبال قومی شاعر کے طور پر معروف ہیں۔"),
            F("meaning of ضرب المثل", "مشہور کہاوت", "صرف نظم", "خط", "خبر", "اصناف", "ضرب المثل ایک مشہور اور بامعنی کہاوت ہوتی ہے۔"),
            F("word used in place of a noun", "ضمیر", "فعل", "حرف جار", "مصدر", "قواعد", "ضمیر اسم کی جگہ استعمال ہوتی ہے۔"),
            F("describing word in Urdu grammar", "صفت", "اسم", "فعل", "حرف عطف", "قواعد", "صفت اسم کی خوبی یا حالت بیان کرتی ہے۔"),
            F("language family of Urdu", "ہند آریائی", "سامی", "ترک", "چینی تبتی", "زبان", "Urdu is generally classified as an Indo-Aryan language.")),

        S("General Knowledge", "Pakistan and world general knowledge for competitive tests",
            F("largest ocean on Earth", "Pacific Ocean", "Atlantic Ocean", "Indian Ocean", "Arctic Ocean", "World", "The Pacific is Earth's largest ocean."),
            F("largest continent by area", "Asia", "Africa", "Europe", "South America", "World", "Asia is the largest continent."),
            F("capital of Türkiye", "Ankara", "Istanbul", "Izmir", "Bursa", "Capitals", "Ankara is the capital of Türkiye."),
            F("capital of China", "Beijing", "Shanghai", "Guangzhou", "Shenzhen", "Capitals", "Beijing is China's capital."),
            F("capital of Saudi Arabia", "Riyadh", "Jeddah", "Makkah", "Madinah", "Capitals", "Riyadh is Saudi Arabia's capital."),
            F("currency of Japan", "yen", "yuan", "won", "ringgit", "Currencies", "Japan uses the yen."),
            F("currency of the United Kingdom", "pound sterling", "euro", "dollar", "franc", "Currencies", "The United Kingdom uses pound sterling."),
            F("largest hot desert", "Sahara", "Gobi", "Kalahari", "Thar", "Geography", "The Sahara is the largest hot desert."),
            F("longest wall structure widely known in China", "Great Wall of China", "Berlin Wall", "Hadrian's Wall", "Western Wall", "World", "The Great Wall is the famous extensive wall system in China."),
            F("country known as the Land of the Rising Sun", "Japan", "China", "Thailand", "Norway", "World", "Japan is widely called the Land of the Rising Sun."),
            F("instrument used to record earthquakes", "seismograph", "barometer", "thermometer", "altimeter", "Science", "A seismograph records seismic motion."),
            F("number of continents in the common model", "seven", "five", "six", "eight", "World", "The commonly taught model has seven continents."),
            F("headquarters country of the International Court of Justice", "Netherlands", "Switzerland", "United States", "France", "International", "The ICJ is in The Hague, Netherlands.")),

        S("Everyday Science", "Science applied to health, home, environment and daily technology",
            F("device converting electrical energy to mechanical rotation", "electric motor", "generator", "transformer", "battery only", "Technology", "An electric motor converts electrical energy into mechanical motion."),
            F("gas commonly used by plants in photosynthesis", "carbon dioxide", "oxygen", "nitrogen", "helium", "Environment", "Plants use carbon dioxide during photosynthesis."),
            F("acid present in the human stomach", "hydrochloric acid", "sulfuric acid", "nitric acid", "acetic acid only", "Health", "The stomach secretes hydrochloric acid."),
            F("main component of natural gas", "methane", "oxygen", "chlorine", "nitrogen", "Energy", "Natural gas consists mainly of methane."),
            F("metal liquid near ordinary room temperature", "mercury", "iron", "aluminium", "copper", "Materials", "Mercury is liquid at typical room temperature."),
            F("vitamin associated with preventing scurvy", "vitamin C", "vitamin D", "vitamin A", "vitamin B12", "Health", "Vitamin C deficiency causes scurvy."),
            F("organ responsible for pumping blood", "heart", "lung", "liver", "kidney", "Health", "The heart pumps blood through the circulatory system."),
            F("process that makes milk safer by controlled heating", "pasteurization", "fermentation only", "freezing", "distillation", "Food Science", "Pasteurization reduces harmful microorganisms through controlled heat."),
            F("renewable energy source among the options", "solar energy", "coal", "petroleum", "natural gas", "Energy", "Sunlight is a renewable energy source."),
            F("colour formed by mixing blue and yellow pigments", "green", "red", "purple", "orange", "Observation", "Blue and yellow pigments commonly mix to green."),
            F("simple machine used to raise a flag", "pulley", "wedge", "screw only", "wheelbarrow", "Machines", "A flagpole commonly uses a pulley."),
            F("unit used for electrical energy billing", "kilowatt-hour", "ampere", "volt", "ohm", "Electricity", "Utilities bill electrical energy in kilowatt-hours."),
            F("safe method for reducing many microbes in drinking water", "boiling", "adding soil", "leaving it open", "mixing oil", "Health", "Boiling can kill many disease-causing microorganisms.")),

        S("Geography", "Physical, human and Pakistani geography",
            F("imaginary line dividing Earth into northern and southern hemispheres", "Equator", "Prime Meridian", "Tropic of Cancer", "International Date Line", "Physical Geography", "The Equator lies at 0 degrees latitude."),
            F("0-degree longitude line", "Prime Meridian", "Equator", "Tropic of Capricorn", "Arctic Circle", "Physical Geography", "The Prime Meridian is 0 degrees longitude."),
            F("largest country by land area", "Russia", "Canada", "China", "United States", "World Geography", "Russia has the largest land area."),
            F("mountain range containing K2", "Karakoram", "Himalayas only", "Hindu Kush", "Sulaiman", "Pakistan Geography", "K2 lies in the Karakoram range."),
            F("sea south of Pakistan", "Arabian Sea", "Red Sea", "Caspian Sea", "Black Sea", "Pakistan Geography", "Pakistan's southern coast borders the Arabian Sea."),
            F("river forming the main axis of Pakistan's river system", "Indus", "Nile", "Amazon", "Danube", "Pakistan Geography", "The Indus is Pakistan's principal river."),
            F("type of rainfall caused by rising heated air", "convectional rainfall", "orographic rainfall", "frontal rainfall", "snowfall only", "Climate", "Surface heating causes air to rise and produce convectional rain."),
            F("instrument measuring humidity", "hygrometer", "barometer", "anemometer", "thermometer", "Climate", "A hygrometer measures humidity."),
            F("instrument measuring wind speed", "anemometer", "hygrometer", "seismograph", "rain gauge", "Climate", "An anemometer measures wind speed."),
            F("largest island in the world excluding continents", "Greenland", "Madagascar", "Borneo", "New Guinea", "World Geography", "Greenland is the largest island by area."),
            F("continent crossed by both Equator and Prime Meridian", "Africa", "Asia", "North America", "Australia", "World Geography", "Both lines cross Africa."),
            F("capital of Balochistan", "Quetta", "Peshawar", "Karachi", "Lahore", "Pakistan Geography", "Quetta is the capital of Balochistan."),
            F("capital of Khyber Pakhtunkhwa", "Peshawar", "Quetta", "Gilgit", "Muzaffarabad", "Pakistan Geography", "Peshawar is the provincial capital.")),

        S("World History", "Major civilisations, events and historical institutions",
            F("civilisation associated with pyramids at Giza", "ancient Egyptian", "Roman", "Indus only", "Mayan only", "Ancient History", "The Giza pyramids were built in ancient Egypt."),
            F("city buried by Mount Vesuvius in 79 CE", "Pompeii", "Athens", "Sparta", "Carthage", "Ancient History", "Vesuvius buried Pompeii in 79 CE."),
            F("empire centred on Rome", "Roman Empire", "Ottoman Empire", "Mughal Empire", "Mauryan Empire", "Ancient History", "Rome was the centre of the Roman Empire."),
            F("document sealed by King John in 1215", "Magna Carta", "Treaty of Versailles", "Bill of Rights 1689", "Domesday Book", "Medieval History", "Magna Carta was sealed in 1215."),
            F("movement beginning in Italy that revived classical learning", "Renaissance", "Industrial Revolution", "Reformation only", "Cold War", "Early Modern", "The Renaissance began in Italian city-states."),
            F("revolution beginning in France in 1789", "French Revolution", "Russian Revolution", "Glorious Revolution", "American Civil War", "Modern History", "The French Revolution began in 1789."),
            F("industrial transformation first associated strongly with Britain", "Industrial Revolution", "Neolithic Revolution", "Green Revolution", "Digital Revolution only", "Modern History", "Industrialisation began earliest at scale in Britain."),
            F("global conflict ending in 1918", "First World War", "Second World War", "Crimean War", "Cold War", "Modern History", "The First World War ended in 1918."),
            F("global conflict ending in 1945", "Second World War", "First World War", "Seven Years' War", "Korean War", "Modern History", "The Second World War ended in 1945."),
            F("organisation founded in 1945 to promote international peace", "United Nations", "League of Nations", "European Union", "NATO", "Modern History", "The United Nations was founded in 1945."),
            F("wall whose fall in 1989 symbolised Cold War change", "Berlin Wall", "Great Wall", "Hadrian's Wall", "Western Wall", "Cold War", "The Berlin Wall fell in 1989."),
            F("ancient civilisation of Mohenjo-daro", "Indus Valley Civilisation", "Roman Civilisation", "Greek Civilisation", "Inca Civilisation", "Ancient History", "Mohenjo-daro was a major Indus Valley city."),
            F("historic route linking East and West trade", "Silk Road", "Amber Room", "Maginot Line", "Panama Route only", "Trade History", "The Silk Road connected trade networks across Eurasia.")),

        S("Pedagogy & Education", "Teaching methods, assessment, classroom management and learning theory",
            F("assessment conducted during instruction to improve learning", "formative assessment", "summative assessment", "placement test only", "final certification", "Assessment", "Formative assessment provides feedback during learning."),
            F("assessment conducted at the end of a course", "summative assessment", "formative assessment", "diagnostic observation only", "peer discussion", "Assessment", "Summative assessment evaluates learning at the end."),
            F("learning objective domain concerned with thinking", "cognitive domain", "affective domain", "psychomotor domain", "social domain only", "Learning", "The cognitive domain concerns knowledge and thinking."),
            F("domain concerned with attitudes and values", "affective domain", "cognitive domain", "psychomotor domain", "numerical domain", "Learning", "The affective domain concerns attitudes, values and feelings."),
            F("domain concerned with physical skills", "psychomotor domain", "affective domain", "cognitive domain", "linguistic domain only", "Learning", "Psychomotor learning involves physical skills."),
            F("strategy where students work in small groups", "cooperative learning", "rote isolation", "lecture only", "silent testing", "Teaching Methods", "Cooperative learning uses structured group work."),
            F("support gradually removed as a learner gains independence", "scaffolding", "punishment", "summative grading", "streaming", "Teaching Methods", "Scaffolding is temporary instructional support."),
            F("test quality describing consistency of scores", "reliability", "validity", "difficulty only", "appearance", "Assessment", "Reliability concerns consistency."),
            F("test quality describing whether it measures the intended construct", "validity", "reliability", "speed", "length", "Assessment", "Validity concerns whether intended learning is measured."),
            F("classroom approach rewarding desired behaviour", "positive reinforcement", "negative transfer", "extinction only", "random punishment", "Classroom Management", "Positive reinforcement strengthens desired behaviour."),
            F("lesson component that activates prior knowledge", "introduction or anticipatory set", "final grade", "detention", "certificate", "Lesson Planning", "A strong opening connects prior and new learning."),
            F("question type allowing students to construct responses", "open-ended question", "true-false only", "matching only", "multiple choice only", "Teaching Methods", "Open-ended questions require constructed responses."),
            F("practice of adapting instruction to learner needs", "differentiated instruction", "uniform pacing only", "ranking only", "exclusion", "Inclusive Education", "Differentiation adapts content, process or product to learners.")),

        S("Logical Reasoning", "Sequences, deduction, classification and verbal logic",
            F("next number in 2, 4, 6, 8", "10", "9", "11", "12", "Series", "The sequence increases by 2."),
            F("odd item among triangle, square, circle, rectangle", "circle", "triangle", "square", "rectangle", "Classification", "A circle has no straight sides."),
            F("conclusion from All cats are mammals and Mimi is a cat", "Mimi is a mammal", "Mimi is a bird", "all mammals are cats", "no conclusion", "Deduction", "Membership transfers through the stated universal rule."),
            F("next letter in A, C, E, G", "I", "H", "J", "K", "Series", "Every second alphabet letter is used."),
            F("missing number in 3, 6, 12, 24", "48", "30", "36", "42", "Series", "Each term doubles."),
            F("relationship where book is to reading as fork is to", "eating", "writing", "sleeping", "driving", "Analogy", "A fork is an instrument used for eating."),
            F("opposite direction of north-east", "south-west", "north-west", "south-east", "east", "Direction", "South-west is opposite north-east."),
            F("day after two days from Monday", "Thursday", "Wednesday", "Friday", "Saturday", "Time Logic", "Two days after Monday is Wednesday; the day after is Thursday."),
            F("next number in 1, 4, 9, 16", "25", "20", "24", "36", "Series", "These are consecutive square numbers."),
            F("odd word among apple, mango, carrot, banana", "carrot", "apple", "mango", "banana", "Classification", "Carrot is a vegetable; the others are fruits."),
            F("valid conclusion from No fish are birds and tuna is a fish", "tuna is not a bird", "tuna is a bird", "all birds are fish", "no fish live in water", "Deduction", "The rule excludes every fish from the bird class."),
            F("next number in 10, 8, 6, 4", "2", "1", "3", "0", "Series", "The sequence decreases by 2."),
            F("coded value if A=1, B=2 and C=3", "3 for C", "1 for C", "2 for C", "4 for C", "Coding", "The stated alphabet code assigns C the value 3.")),

        S("Analytical Reasoning", "Structured scenarios, ordering, grouping and constraints",
            F("earliest position if Ali must come before Bilal", "Ali before Bilal", "Bilal before Ali", "both at the same position", "order cannot ever be set", "Ordering", "The constraint directly places Ali earlier than Bilal."),
            F("maximum members in a team with limit five", "five", "four only", "six", "unlimited", "Grouping", "A maximum of five permits at most five members."),
            F("minimum number satisfying greater than 7 for integers", "8", "7", "6", "9 only", "Constraints", "Eight is the smallest integer greater than seven."),
            F("result when all three required conditions are true", "the rule is satisfied", "the rule always fails", "conditions are irrelevant", "only one condition matters", "Conditions", "A rule requiring all conditions is met when all are true."),
            F("possible order when A precedes B and B precedes C", "A-B-C", "C-B-A", "B-A-C", "C-A-B", "Ordering", "Transitive ordering requires A before B before C."),
            F("number of ways to choose one leader from four people", "4", "2", "8", "16", "Selection", "Any one of four people may be leader."),
            F("remaining seats when 3 of 8 seats are filled", "5", "3", "8", "11", "Grouping", "Eight minus three leaves five."),
            F("necessary outcome if exactly one of P and Q is true and P is true", "Q is false", "Q is true", "both are true", "neither has a value", "Logic", "Exactly one true makes Q false when P is true."),
            F("possible group size under at least 2 and at most 4", "3", "1", "5", "6", "Grouping", "Three lies within the allowed range."),
            F("middle position in an ordered list of five", "third", "second", "fourth", "fifth", "Ordering", "The third item has two before and two after."),
            F("consequence if X cannot be with Y", "they must be in different groups", "they must be together", "X must be removed", "Y must lead", "Grouping", "The restriction prevents X and Y sharing a group."),
            F("latest possible position before a fixed fourth-place item", "third", "fourth", "fifth", "first only", "Ordering", "The last position before fourth is third."),
            F("status of a conclusion contradicted by a stated rule", "invalid", "necessarily true", "both true and false", "automatically preferred", "Logic", "A conclusion that violates a rule is invalid.")),

        S("Quantitative Reasoning", "Percentages, ratios, averages, rates and applied arithmetic",
            F("10 percent of 350", "35", "30", "40", "45", "Percentages", "0.10 × 350 = 35."),
            F("ratio 8:12 in simplest form", "2:3", "4:5", "3:2", "8:6", "Ratios", "Dividing both terms by 4 gives 2:3."),
            F("average of 10, 20 and 30", "20", "15", "25", "30", "Averages", "The sum 60 divided by 3 is 20."),
            F("distance travelled at 60 km/h for 2 hours", "120 km", "30 km", "60 km", "180 km", "Rates", "Distance equals speed multiplied by time."),
            F("sale price after a 20 percent discount on Rs 1,000", "Rs 800", "Rs 200", "Rs 1,200", "Rs 900", "Percentages", "A 20 percent discount is Rs 200, leaving Rs 800."),
            F("simple interest on Rs 1,000 at 10 percent for one year", "Rs 100", "Rs 10", "Rs 110", "Rs 1,100", "Finance", "Simple interest is P × r × t = 100."),
            F("fraction equivalent to 0.75", "3/4", "1/4", "2/3", "4/3", "Fractions", "0.75 equals 75/100 or 3/4."),
            F("value after increasing 200 by 15 percent", "230", "215", "225", "240", "Percentages", "Fifteen percent of 200 is 30."),
            F("time required for 150 km at 50 km/h", "3 hours", "2 hours", "4 hours", "5 hours", "Rates", "Time equals distance divided by speed."),
            F("number of 5-person teams formed from exactly 5 people", "1", "5", "10", "25", "Counting", "All five must form the only possible team."),
            F("unit price when 4 notebooks cost Rs 200", "Rs 50", "Rs 40", "Rs 80", "Rs 100", "Rates", "200 divided by 4 equals 50."),
            F("value of 2/5 of 100", "40", "20", "25", "50", "Fractions", "100 × 2/5 = 40."),
            F("percentage represented by 45 out of 60", "75%", "60%", "70%", "80%", "Percentages", "45 ÷ 60 × 100 = 75 percent.")),

        S("Basic Statistics", "Descriptive statistics, probability and data interpretation",
            F("mean of 2, 4, 6 and 8", "5", "4", "6", "20", "Central Tendency", "The sum 20 divided by 4 is 5."),
            F("median of 1, 3, 5, 7, 9", "5", "3", "7", "9", "Central Tendency", "Five is the middle ordered value."),
            F("mode of 2, 2, 3, 4", "2", "3", "4", "no mode", "Central Tendency", "Two occurs most often."),
            F("range of 4, 9, 12", "8", "4", "12", "25", "Dispersion", "Range is maximum minus minimum: 12 - 4 = 8."),
            F("probability of rolling a 6 on a fair die", "1/6", "1/2", "1/3", "1", "Probability", "One of six equally likely outcomes is a six."),
            F("probability value for an impossible event", "0", "1", "-1", "100", "Probability", "An impossible event has probability zero."),
            F("probability value for a certain event", "1", "0", "1/2", "-1", "Probability", "A certain event has probability one."),
            F("chart best known for showing parts of a whole", "pie chart", "scatter plot", "histogram only", "line of best fit", "Data Display", "A pie chart displays proportions of a whole."),
            F("graph showing relationship between two numerical variables", "scatter plot", "pie chart", "pictogram only", "frequency table only", "Data Display", "A scatter plot shows paired numerical observations."),
            F("square root of variance", "standard deviation", "mean", "median", "range", "Dispersion", "Standard deviation is the square root of variance."),
            F("total probability across all mutually exclusive outcomes", "1", "0", "50", "100", "Probability", "Probabilities of all exhaustive outcomes sum to one."),
            F("sample selected so each member has equal chance", "simple random sample", "convenience sample", "judgment sample", "quota only", "Sampling", "Simple random sampling gives each member an equal selection chance."),
            F("value separating the lower and upper halves of ordered data", "median", "mean", "mode", "variance", "Central Tendency", "The median is the middle position measure.")),

        S("Constitution & Civics", "Pakistan's constitutional structure, citizenship and public institutions",
            F("supreme law of Pakistan", "Constitution of Pakistan", "a ministry circular", "a local notice", "a private contract", "Constitution", "The Constitution is the supreme legal framework."),
            F("year of the Constitution presently in force", "1973", "1956", "1962", "1940", "Constitution", "Pakistan's current Constitution was enacted in 1973."),
            F("lower house of Parliament", "National Assembly", "Senate", "Supreme Court", "Election Commission", "Parliament", "The National Assembly is the lower house."),
            F("upper house of Parliament", "Senate", "National Assembly", "Cabinet Division", "Council of Common Interests only", "Parliament", "The Senate is the upper house."),
            F("head of state in Pakistan", "President", "Prime Minister", "Chief Justice", "Chairman Senate", "State Structure", "The President is the constitutional head of state."),
            F("head of the federal government", "Prime Minister", "President", "Governor", "Speaker only", "State Structure", "The Prime Minister heads the federal government."),
            F("institution conducting general elections", "Election Commission of Pakistan", "State Bank of Pakistan", "Federal Board of Revenue", "Council of Islamic Ideology", "Institutions", "The Election Commission conducts elections."),
            F("highest court of Pakistan", "Supreme Court", "High Court", "District Court", "Federal Shariat Court only", "Judiciary", "The Supreme Court is the apex court."),
            F("constitutional name of Pakistan", "Islamic Republic of Pakistan", "Federal Kingdom of Pakistan", "Republic of Indus", "Union of Pakistan only", "Constitution", "The Constitution names the state the Islamic Republic of Pakistan."),
            F("minimum voting age for general elections", "18 years", "16 years", "20 years", "21 years", "Elections", "Pakistani citizens aged 18 or above may register subject to law."),
            F("government level closest to municipal services", "local government", "federal cabinet", "Supreme Court", "Senate secretariat", "Government", "Local government manages many community and municipal functions."),
            F("principle that everyone is subject to law", "rule of law", "absolute discretion", "hereditary privilege", "martial custom", "Civics", "Rule of law requires public and private actors to be governed by law."),
            F("official responsible for leading a provincial government", "Chief Minister", "Governor-General", "Mayor of Pakistan", "Chief Election Commissioner", "Provincial Government", "The Chief Minister leads the elected provincial government."))
    ];

    private static SubjectFacts S(string name, string description, params Fact[] facts) => new(name, description, facts);
    private static Fact F(string stem, string answer, string wrong1, string wrong2, string wrong3, string topic, string explanation) => new(stem, answer, wrong1, wrong2, wrong3, topic, explanation);
}
