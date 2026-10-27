using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Data;

/// <summary>
/// Built-in starter question bank for the most common competitive/academic subjects.
/// The seed is idempotent: existing subjects/questions are preserved and only missing
/// subjects or missing question texts are inserted.
/// </summary>
public static class QuestionBankSeed
{
    private sealed record SeedQuestion(
        string Text,
        string A,
        string B,
        string C,
        string D,
        string Answer,
        string Topic,
        string Explanation);

    private sealed record SeedSubject(
        string Name,
        string Description,
        SeedQuestion[] Questions);

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        var seed = BuildSeed();

        foreach (var subjectSeed in seed)
        {
            var subject = await context.Subjects
                .FirstOrDefaultAsync(x => x.SubjectName == subjectSeed.Name);

            if (subject == null)
            {
                subject = new Subject
                {
                    SubjectName = subjectSeed.Name,
                    Description = subjectSeed.Description,
                    IsActive = true,
                    IsLocked = false,
                    CreatedDate = DateTime.Now
                };

                context.Subjects.Add(subject);
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded subject {Subject}.", subjectSeed.Name);
            }
            else if (!subject.IsActive)
            {
                subject.IsActive = true;
                subject.UpdatedDate = DateTime.Now;
                await context.SaveChangesAsync();
            }

            var existingTexts = await context.Questions
                .Where(x => x.SubjectId == subject.SubjectId)
                .Select(x => x.QuestionText)
                .ToListAsync();

            var existing = existingTexts
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var nextOrder = await context.Questions
                .Where(x => x.SubjectId == subject.SubjectId)
                .Select(x => (int?)x.DisplayOrder)
                .MaxAsync() ?? 0;

            var additions = new List<Question>();

            foreach (var item in subjectSeed.Questions)
            {
                if (!existing.Add(item.Text))
                    continue;

                nextOrder++;

                additions.Add(new Question
                {
                    SubjectId = subject.SubjectId,
                    QuestionText = item.Text,
                    OptionA = item.A,
                    OptionB = item.B,
                    OptionC = item.C,
                    OptionD = item.D,
                    OptionE = null,
                    CorrectAnswer = item.Answer,
                    Topic = item.Topic,
                    Tags = $"Seed,{subjectSeed.Name}",
                    Explanation = item.Explanation,
                    DisplayOrder = nextOrder,
                    IsActive = true,
                    IsVerified = true,
                    IsLocked = false,
                    CreatedDate = DateTime.Now,
                    CreatedBy = "System Seed"
                });
            }

            if (additions.Count > 0)
            {
                context.Questions.AddRange(additions);
                await context.SaveChangesAsync();
                logger.LogInformation(
                    "Seeded {Count} questions for {Subject}.",
                    additions.Count,
                    subjectSeed.Name);
            }
        }
    }

    private static SeedSubject[] BuildSeed() =>
    [
        new("Physics", "Core physics MCQs covering mechanics, heat, waves, electricity, magnetism and modern physics.",
        [
            Q("The SI unit of force is:", "Joule", "Newton", "Watt", "Pascal", "B", "Mechanics", "Force is measured in newtons (N)."),
            Q("The acceleration due to gravity near Earth's surface is approximately:", "4.9 m/s²", "8.0 m/s²", "9.8 m/s²", "12.0 m/s²", "C", "Mechanics", "Standard gravitational acceleration is about 9.8 m/s²."),
            Q("Which quantity has both magnitude and direction?", "Mass", "Speed", "Temperature", "Velocity", "D", "Mechanics", "Velocity is a vector quantity."),
            Q("The SI unit of power is:", "Watt", "Volt", "Ohm", "Coulomb", "A", "Energy", "Power is measured in watts (W)."),
            Q("Kinetic energy of an object is given by:", "mv", "mgh", "½mv²", "F/t", "C", "Energy", "Kinetic energy equals one-half mass times velocity squared."),
            Q("Which law states that every action has an equal and opposite reaction?", "Newton's first law", "Newton's second law", "Newton's third law", "Hooke's law", "C", "Mechanics", "Newton's third law describes action-reaction pairs."),
            Q("The unit of electrical resistance is:", "Ohm", "Ampere", "Volt", "Tesla", "A", "Electricity", "Resistance is measured in ohms (Ω)."),
            Q("Ohm's law is represented by:", "V = IR", "P = VI", "Q = It", "F = ma", "A", "Electricity", "Ohm's law states V = IR."),
            Q("A device used to measure electric current is a:", "Voltmeter", "Ammeter", "Barometer", "Thermometer", "B", "Electricity", "An ammeter measures current in amperes."),
            Q("The speed of light in vacuum is approximately:", "3 × 10⁶ m/s", "3 × 10⁷ m/s", "3 × 10⁸ m/s", "3 × 10⁹ m/s", "C", "Waves", "Light travels at about 3 × 10⁸ m/s in vacuum."),
            Q("Which wave can travel through vacuum?", "Sound wave", "Water wave", "Light wave", "Seismic wave", "C", "Waves", "Electromagnetic waves such as light do not require a material medium."),
            Q("The frequency of a wave is measured in:", "Hertz", "Newton", "Joule", "Tesla", "A", "Waves", "Frequency is measured in hertz (Hz)."),
            Q("Which mirror is commonly used as a rear-view mirror in vehicles?", "Plane", "Convex", "Concave", "Parabolic", "B", "Optics", "A convex mirror provides a wider field of view."),
            Q("The splitting of white light into its component colours is called:", "Reflection", "Refraction", "Dispersion", "Diffraction", "C", "Optics", "Dispersion separates white light into its spectrum."),
            Q("The boiling point of pure water at standard atmospheric pressure is:", "0°C", "50°C", "100°C", "212°C below zero", "C", "Thermal Physics", "Water boils at 100°C at 1 atmosphere."),
            Q("Heat transfer through direct contact is called:", "Convection", "Radiation", "Conduction", "Evaporation", "C", "Thermal Physics", "Conduction transfers heat through direct molecular interaction."),
            Q("Which particle has a negative electric charge?", "Proton", "Neutron", "Electron", "Positron", "C", "Modern Physics", "Electrons carry negative charge."),
            Q("The atomic nucleus contains:", "Only electrons", "Protons and neutrons", "Only neutrons", "Electrons and photons", "B", "Modern Physics", "The nucleus is made mainly of protons and neutrons."),
            Q("Half-life is the time required for:", "All nuclei to decay", "Half the radioactive nuclei to decay", "One nucleus to decay", "Radiation to stop completely", "B", "Nuclear Physics", "Half-life is the time for half of a radioactive sample to decay."),
            Q("The SI unit of magnetic field strength (magnetic flux density) is:", "Tesla", "Weber", "Henry", "Farad", "A", "Magnetism", "Magnetic flux density is measured in tesla (T).")
        ]),

        new("Chemistry", "Chemistry MCQs covering atoms, periodic table, bonding, acids, bases, reactions, organic and physical chemistry.",
        [
            Q("The atomic number of an element equals the number of:", "Neutrons", "Protons", "Nucleons", "Shells", "B", "Atomic Structure", "Atomic number is the number of protons in the nucleus."),
            Q("The chemical symbol for sodium is:", "S", "So", "Na", "N", "C", "Periodic Table", "Sodium is represented by Na."),
            Q("The pH of a neutral solution at 25°C is:", "0", "5", "7", "14", "C", "Acids and Bases", "A neutral solution has pH 7 at 25°C."),
            Q("Which gas is released when an acid reacts with a carbonate?", "Oxygen", "Hydrogen", "Carbon dioxide", "Nitrogen", "C", "Chemical Reactions", "Acid-carbonate reactions release carbon dioxide."),
            Q("H₂O is commonly known as:", "Hydrogen peroxide", "Water", "Hydrogen oxide gas", "Ozone", "B", "Basic Chemistry", "H₂O is water."),
            Q("Which element has the chemical symbol Fe?", "Fluorine", "Iron", "Francium", "Fermium", "B", "Periodic Table", "Fe is the symbol for iron."),
            Q("A substance that speeds up a chemical reaction without being consumed is a:", "Reactant", "Catalyst", "Solvent", "Product", "B", "Chemical Kinetics", "A catalyst changes reaction rate without being consumed overall."),
            Q("The bond formed by transfer of electrons is called:", "Covalent bond", "Ionic bond", "Metallic bond", "Hydrogen bond", "B", "Chemical Bonding", "Ionic bonding involves electron transfer and electrostatic attraction."),
            Q("The bond formed by sharing electrons is called:", "Ionic", "Covalent", "Metallic", "Coordinate only", "B", "Chemical Bonding", "Covalent bonds involve shared electron pairs."),
            Q("Which gas is most abundant in Earth's atmosphere?", "Oxygen", "Carbon dioxide", "Nitrogen", "Hydrogen", "C", "Environmental Chemistry", "Nitrogen makes up about 78% of Earth's atmosphere."),
            Q("The molar mass of H₂O is approximately:", "10 g/mol", "18 g/mol", "22 g/mol", "36 g/mol", "B", "Mole Concept", "Two hydrogen atoms plus one oxygen atom give about 18 g/mol."),
            Q("Avogadro's number is approximately:", "6.022 × 10²³", "9.81 × 10²", "3.00 × 10⁸", "1.602 × 10⁻¹⁹", "A", "Mole Concept", "One mole contains approximately 6.022 × 10²³ particles."),
            Q("Which is a strong acid?", "HCl", "CH₃COOH", "H₂O", "NH₃", "A", "Acids and Bases", "Hydrochloric acid is a strong acid in aqueous solution."),
            Q("Which is a common strong base?", "NaOH", "H₂CO₃", "HCl", "CO₂", "A", "Acids and Bases", "Sodium hydroxide is a strong base."),
            Q("Oxidation is commonly defined as:", "Gain of electrons", "Loss of electrons", "Gain of neutrons", "Loss of protons only", "B", "Redox", "Oxidation is loss of electrons; reduction is gain."),
            Q("Which element is a halogen?", "Sodium", "Chlorine", "Calcium", "Aluminium", "B", "Periodic Table", "Chlorine belongs to Group 17, the halogens."),
            Q("The functional group -OH is called:", "Carbonyl", "Hydroxyl", "Amino", "Carboxyl", "B", "Organic Chemistry", "The -OH group is the hydroxyl group."),
            Q("The simplest alkane is:", "Ethene", "Methane", "Ethyne", "Benzene", "B", "Organic Chemistry", "Methane (CH₄) is the simplest alkane."),
            Q("Which process separates a soluble solid from a solution by forming crystals?", "Filtration", "Crystallization", "Decantation", "Sublimation only", "B", "Separation Techniques", "Crystallization recovers a dissolved solid as crystals."),
            Q("Rusting of iron requires oxygen and:", "Helium", "Water/moisture", "Nitrogen only", "Argon", "B", "Environmental Chemistry", "Iron rusting requires oxygen and moisture." )
        ]),

        new("Biology", "Biology MCQs covering cells, genetics, human biology, plants, ecology and microorganisms.",
        [
            Q("The basic structural and functional unit of life is the:", "Tissue", "Organ", "Cell", "Organ system", "C", "Cell Biology", "The cell is the basic unit of life."),
            Q("Which organelle is known as the powerhouse of the cell?", "Nucleus", "Ribosome", "Mitochondrion", "Golgi apparatus", "C", "Cell Biology", "Mitochondria generate much of the cell's usable ATP."),
            Q("DNA stands for:", "Deoxyribonucleic acid", "Dinitrogen acid", "Double nucleic acid", "Deoxygenated nitrogen acid", "A", "Genetics", "DNA means deoxyribonucleic acid."),
            Q("Which organelle contains most of a eukaryotic cell's genetic material?", "Nucleus", "Lysosome", "Vacuole", "Cell wall", "A", "Cell Biology", "The nucleus contains the chromosomes in eukaryotic cells."),
            Q("Photosynthesis mainly occurs in the:", "Mitochondria", "Chloroplasts", "Ribosomes", "Nucleus", "B", "Plant Biology", "Chloroplasts contain chlorophyll and are the main site of photosynthesis."),
            Q("The green pigment required for photosynthesis is:", "Haemoglobin", "Chlorophyll", "Melanin", "Keratin", "B", "Plant Biology", "Chlorophyll absorbs light energy for photosynthesis."),
            Q("Which gas is taken in by plants during photosynthesis?", "Oxygen", "Nitrogen", "Carbon dioxide", "Hydrogen", "C", "Plant Biology", "Plants use carbon dioxide to synthesize carbohydrates."),
            Q("The human heart normally has how many chambers?", "2", "3", "4", "5", "C", "Human Biology", "The heart has two atria and two ventricles."),
            Q("Red blood cells primarily transport:", "Hormones only", "Oxygen", "Digestive enzymes", "Bile", "B", "Human Biology", "Haemoglobin in red blood cells carries oxygen."),
            Q("The largest organ of the human body is the:", "Liver", "Skin", "Lung", "Brain", "B", "Human Biology", "Skin is the largest organ by surface area."),
            Q("Which organ filters blood and produces urine?", "Heart", "Kidney", "Stomach", "Pancreas", "B", "Human Biology", "The kidneys filter blood and form urine."),
            Q("Insulin is produced mainly by the:", "Thyroid gland", "Pancreas", "Adrenal gland", "Pituitary gland", "B", "Human Biology", "Pancreatic beta cells produce insulin."),
            Q("A gene is best described as:", "A type of cell", "A segment of DNA carrying hereditary information", "A protein only", "A blood cell", "B", "Genetics", "Genes are DNA sequences that contribute to inherited traits and biological functions."),
            Q("The process by which cells divide to produce two genetically similar daughter cells is:", "Meiosis", "Mitosis", "Fertilization", "Transcription", "B", "Cell Division", "Mitosis produces two generally genetically similar daughter cells."),
            Q("Meiosis is important because it produces:", "Body cells with doubled chromosomes", "Gametes with reduced chromosome number", "Only bacterial cells", "Identical organs", "B", "Genetics", "Meiosis produces haploid gametes and increases genetic variation."),
            Q("Which blood group is known as the universal red-cell donor in the ABO/Rh system when Rh-negative?", "AB positive", "O negative", "A positive", "B positive", "B", "Human Biology", "O-negative red cells are commonly used as the universal donor type."),
            Q("Bacteria are generally classified as:", "Prokaryotes", "Eukaryotes", "Viruses", "Multicellular animals", "A", "Microbiology", "Bacteria are prokaryotic organisms without a membrane-bound nucleus."),
            Q("A virus can reproduce only by:", "Using sunlight directly", "Replicating inside a suitable host cell", "Photosynthesis", "Making its own ribosomes", "B", "Microbiology", "Viruses depend on host cells for replication."),
            Q("The transfer of pollen from anther to stigma is called:", "Germination", "Pollination", "Fertilization", "Transpiration", "B", "Plant Biology", "Pollination is the transfer of pollen to the stigma."),
            Q("The process by which water vapour leaves plant leaves is called:", "Respiration", "Transpiration", "Digestion", "Osmosis", "B", "Plant Biology", "Transpiration is water loss as vapour, mainly through stomata." )
        ]),

        new("English", "English language MCQs covering grammar, vocabulary, sentence structure, tenses, voice and common usage.",
        [
            Q("Choose the correct sentence:", "He go to school every day.", "He goes to school every day.", "He going to school every day.", "He gone to school every day.", "B", "Grammar", "With third-person singular subject he, the present simple verb takes -s."),
            Q("The past tense of 'go' is:", "Goed", "Gone", "Went", "Going", "C", "Tenses", "The simple past form of go is went."),
            Q("Choose the synonym of 'rapid':", "Slow", "Fast", "Weak", "Late", "B", "Vocabulary", "Rapid means fast or quick."),
            Q("Choose the antonym of 'ancient':", "Old", "Modern", "Historic", "Former", "B", "Vocabulary", "Modern is the opposite of ancient."),
            Q("Which word is a noun?", "Beautiful", "Quickly", "Honesty", "Run", "C", "Parts of Speech", "Honesty is an abstract noun."),
            Q("Which word is an adjective?", "Beauty", "Beautiful", "Beautifully", "Beautify", "B", "Parts of Speech", "Beautiful describes a noun and functions as an adjective."),
            Q("Choose the correct article: 'He is ___ honest man.'", "a", "an", "the", "no article", "B", "Articles", "Honest begins with a vowel sound, so 'an' is used."),
            Q("Choose the correct preposition: 'She is good ___ mathematics.'", "in", "at", "on", "for", "B", "Prepositions", "The standard expression is 'good at mathematics'."),
            Q("What is the plural of 'child'?", "Childs", "Childes", "Children", "Childrens", "C", "Vocabulary", "Children is the irregular plural of child."),
            Q("Which sentence is in the present continuous tense?", "She writes a letter.", "She wrote a letter.", "She is writing a letter.", "She has written a letter.", "C", "Tenses", "Present continuous uses am/is/are + present participle."),
            Q("Change to passive voice: 'Ali wrote a letter.'", "A letter was written by Ali.", "A letter is written by Ali.", "Ali was written a letter.", "A letter has written Ali.", "A", "Voice", "The simple past passive uses was/were + past participle."),
            Q("Choose the correct indirect speech: He said, 'I am tired.'", "He said that I am tired.", "He said that he was tired.", "He says that he was tired.", "He said that he is tired yesterday.", "B", "Narration", "In standard reported speech, present 'am' shifts to past 'was'."),
            Q("A person who writes poems is called a:", "Poet", "Poem", "Poetic", "Publisher", "A", "Vocabulary", "A poet writes poems."),
            Q("Choose the correct spelling:", "Accomodation", "Acommodation", "Accommodation", "Accommadation", "C", "Spelling", "The correct spelling is accommodation."),
            Q("Which punctuation mark ends a direct question?", "Comma", "Full stop", "Question mark", "Colon", "C", "Punctuation", "A direct question normally ends with a question mark."),
            Q("'Although' is primarily a:", "Conjunction", "Noun", "Pronoun", "Interjection", "A", "Parts of Speech", "Although introduces a subordinate clause and is a conjunction."),
            Q("Choose the correct comparative form of 'good':", "Gooder", "More good", "Better", "Best", "C", "Degrees", "The comparative of good is better."),
            Q("Which sentence is grammatically correct?", "Neither Ali nor Ahmed are present.", "Neither Ali nor Ahmed is present.", "Neither Ali or Ahmed is present.", "Neither Ali nor Ahmed were present.", "B", "Subject-Verb Agreement", "With neither...nor, a singular pair takes a singular verb here."),
            Q("The idiom 'once in a blue moon' means:", "Every day", "Very rarely", "At night", "Very loudly", "B", "Idioms", "The idiom means something happens very rarely."),
            Q("Choose the correct word: 'The medicine had a positive ___ on him.'", "affect", "effect", "effects", "affecting", "B", "Vocabulary", "Effect is the noun meaning result or consequence." )
        ]),

        new("General Science", "General science MCQs covering earth science, environment, basic chemistry, physics, astronomy and health science.",
        [
            Q("The planet known as the Red Planet is:", "Venus", "Mars", "Jupiter", "Mercury", "B", "Astronomy", "Mars appears reddish because of iron-rich minerals on its surface."),
            Q("Earth's natural satellite is the:", "Sun", "Moon", "Mars", "Venus", "B", "Astronomy", "The Moon is Earth's natural satellite."),
            Q("The layer of atmosphere where most weather occurs is:", "Stratosphere", "Troposphere", "Mesosphere", "Thermosphere", "B", "Earth Science", "Most clouds and weather occur in the troposphere."),
            Q("The process by which liquid water changes into vapour is:", "Condensation", "Evaporation", "Freezing", "Deposition", "B", "Water Cycle", "Evaporation changes liquid water into water vapour."),
            Q("Which vitamin is produced in the skin in response to sunlight?", "Vitamin A", "Vitamin B12", "Vitamin C", "Vitamin D", "D", "Health Science", "Sunlight helps the skin synthesize vitamin D."),
            Q("Which organ is primarily responsible for pumping blood?", "Lung", "Heart", "Kidney", "Stomach", "B", "Human Science", "The heart pumps blood through the circulatory system."),
            Q("Which gas is essential for human respiration?", "Oxygen", "Carbon dioxide", "Methane", "Helium", "A", "Health Science", "Human cells use oxygen for aerobic respiration."),
            Q("The freezing point of pure water at standard pressure is:", "0°C", "10°C", "50°C", "100°C", "A", "Matter", "Pure water freezes at 0°C at standard pressure."),
            Q("Which instrument measures atmospheric pressure?", "Thermometer", "Barometer", "Ammeter", "Hygrometer", "B", "Earth Science", "A barometer measures atmospheric pressure."),
            Q("The closest star to Earth is:", "Sirius", "Polaris", "The Sun", "Alpha Centauri", "C", "Astronomy", "The Sun is Earth's nearest star."),
            Q("The force that pulls objects toward Earth is:", "Magnetism only", "Gravity", "Friction", "Buoyancy", "B", "Physics", "Gravity attracts masses toward one another, including objects toward Earth."),
            Q("Which renewable energy source uses moving air?", "Solar", "Wind", "Geothermal", "Biomass only", "B", "Energy", "Wind turbines convert wind energy into electrical energy."),
            Q("The ozone layer helps protect life from harmful:", "Infrared radiation", "Ultraviolet radiation", "Radio waves", "Sound waves", "B", "Environment", "Stratospheric ozone absorbs much of the Sun's harmful UV radiation."),
            Q("Which is a greenhouse gas?", "Carbon dioxide", "Oxygen", "Argon", "Helium", "A", "Environment", "Carbon dioxide is an important greenhouse gas."),
            Q("The main source of energy for Earth's climate system is:", "The Moon", "The Sun", "Volcanoes", "Earthquakes", "B", "Earth Science", "Solar radiation is the primary external energy source for Earth's climate system."),
            Q("Which blood cells help fight infections?", "Red blood cells", "White blood cells", "Platelets", "Plasma only", "B", "Health Science", "White blood cells are major components of the immune response."),
            Q("A balanced diet should provide:", "Only carbohydrates", "Only proteins", "A suitable variety of nutrients", "Only fats", "C", "Health Science", "A balanced diet supplies carbohydrates, proteins, fats, vitamins, minerals, fibre and water in suitable amounts."),
            Q("Which simple machine is a rigid bar that pivots around a fulcrum?", "Lever", "Pulley only", "Screw", "Wedge", "A", "Physics", "A lever is a rigid bar rotating about a fulcrum."),
            Q("Earth completes one rotation on its axis in approximately:", "12 hours", "24 hours", "30 days", "365 days", "B", "Astronomy", "One rotation relative to the Sun is approximately 24 hours."),
            Q("Earth completes one revolution around the Sun in approximately:", "24 hours", "30 days", "365.25 days", "10 years", "C", "Astronomy", "One orbit takes about 365.25 days, defining a year." )
        ]),

        new("Current Affairs", "Starter current-affairs and general-awareness MCQs focused mainly on major international and Pakistani developments from 2023-2025.",
        [
            Q("The 2024 Summer Olympics were held in:", "Tokyo", "Paris", "London", "Los Angeles", "B", "International", "Paris hosted the 2024 Summer Olympics."),
            Q("The 2026 FIFA World Cup is scheduled to be hosted by:", "Brazil only", "Canada, Mexico and the United States", "Qatar and UAE", "Spain and Portugal", "B", "Sports", "The 2026 World Cup is jointly hosted by Canada, Mexico and the United States."),
            Q("COP28 was held in:", "Dubai, United Arab Emirates", "Paris, France", "Glasgow, UK", "New York, USA", "A", "Climate", "COP28 took place in Dubai in 2023."),
            Q("The headquarters of the United Nations is in:", "Geneva only", "New York City", "London", "Paris", "B", "International Organizations", "The UN's main headquarters is in New York City."),
            Q("The International Court of Justice is based in:", "The Hague", "Geneva", "Rome", "Vienna", "A", "International Organizations", "The ICJ is based at the Peace Palace in The Hague, Netherlands."),
            Q("The headquarters of the World Health Organization is in:", "Geneva", "New York", "Nairobi", "Brussels", "A", "International Organizations", "WHO headquarters are in Geneva, Switzerland."),
            Q("The headquarters of the International Monetary Fund is in:", "Washington, D.C.", "London", "Paris", "Beijing", "A", "International Organizations", "The IMF is headquartered in Washington, D.C."),
            Q("The currency of Japan is:", "Won", "Yen", "Yuan", "Ringgit", "B", "World", "Japan uses the yen."),
            Q("The 2024 Nobel Peace Prize was awarded to:", "Nihon Hidankyo", "WHO", "UNICEF", "Red Cross", "A", "World", "Nihon Hidankyo received the 2024 Nobel Peace Prize."),
            Q("The 2024 Nobel Prize in Literature was awarded to:", "Han Kang", "Annie Ernaux", "Jon Fosse", "Abdulrazak Gurnah", "A", "World", "South Korean writer Han Kang won the 2024 Nobel Prize in Literature."),
            Q("The 2024 Nobel Prize in Physics was awarded to John Hopfield and:", "Geoffrey Hinton", "Roger Penrose", "Demis Hassabis", "Tim Berners-Lee", "A", "Science", "Geoffrey Hinton shared the 2024 Physics Nobel with John Hopfield."),
            Q("The 2024 Nobel Prize in Chemistry recognized work involving protein design and:", "Protein structure prediction", "Black-hole imaging", "Ocean tides", "Plate tectonics", "A", "Science", "The chemistry prize recognized protein design and computational protein structure prediction."),
            Q("The 2024 Summer Paralympics were held in:", "Paris", "Tokyo", "Rome", "Berlin", "A", "Sports", "Paris hosted the 2024 Summer Paralympics."),
            Q("Pakistan's national legislature is called the:", "Congress", "Parliament", "Duma", "Knesset", "B", "Pakistan", "Pakistan's Parliament consists of the National Assembly and Senate."),
            Q("The capital of Pakistan is:", "Karachi", "Lahore", "Islamabad", "Peshawar", "C", "Pakistan", "Islamabad is Pakistan's federal capital."),
            Q("The China-Pakistan Economic Corridor is commonly abbreviated as:", "CPEC", "CPAC", "PCEC", "CEPC", "A", "Pakistan", "CPEC stands for China-Pakistan Economic Corridor."),
            Q("The G20 is primarily a forum for major:", "Religious organizations", "Economies", "Universities", "Sports clubs", "B", "International Economics", "The G20 is a forum of major economies."),
            Q("The Sustainable Development Goals were adopted by UN member states in:", "2000", "2010", "2015", "2020", "C", "International Development", "The 17 SDGs were adopted in 2015 as part of the 2030 Agenda."),
            Q("World Environment Day is observed annually on:", "5 June", "14 August", "1 May", "10 December", "A", "Environment", "World Environment Day is observed on 5 June."),
            Q("International Women's Day is observed annually on:", "8 March", "23 March", "1 July", "25 December", "A", "International", "International Women's Day is observed on 8 March." )
        ]),

        new("Pakistan Studies", "Pakistan Studies MCQs covering history, geography, constitution, national institutions, culture and important milestones.",
        [
            Q("Pakistan came into being on:", "14 August 1947", "23 March 1940", "15 August 1947", "11 September 1948", "A", "History", "Pakistan became independent on 14 August 1947."),
            Q("The Lahore Resolution was passed in:", "1930", "1940", "1947", "1956", "B", "History", "The Lahore Resolution was adopted on 23 March 1940."),
            Q("The first Governor-General of Pakistan was:", "Liaquat Ali Khan", "Muhammad Ali Jinnah", "Ayub Khan", "Iskander Mirza", "B", "History", "Muhammad Ali Jinnah served as Pakistan's first Governor-General."),
            Q("The first Prime Minister of Pakistan was:", "Liaquat Ali Khan", "Khawaja Nazimuddin", "Suhrawardy", "Muhammad Ali Bogra", "A", "History", "Liaquat Ali Khan was Pakistan's first Prime Minister."),
            Q("Pakistan's first constitution was enforced in:", "1947", "1956", "1962", "1973", "B", "Constitution", "The first Constitution of Pakistan came into force in 1956."),
            Q("The Constitution currently in force in Pakistan was originally enacted in:", "1956", "1962", "1973", "1985", "C", "Constitution", "The 1973 Constitution remains the constitutional framework, subject to amendments."),
            Q("The national language of Pakistan is:", "Punjabi", "Sindhi", "Urdu", "English", "C", "Constitution", "Urdu is Pakistan's national language."),
            Q("The federal capital of Pakistan is:", "Karachi", "Islamabad", "Lahore", "Quetta", "B", "Geography", "Islamabad is the federal capital."),
            Q("The largest province of Pakistan by area is:", "Punjab", "Sindh", "Balochistan", "Khyber Pakhtunkhwa", "C", "Geography", "Balochistan is the largest province by land area."),
            Q("The most populous province of Pakistan is:", "Sindh", "Punjab", "Balochistan", "Khyber Pakhtunkhwa", "B", "Geography", "Punjab is Pakistan's most populous province."),
            Q("The highest mountain in Pakistan is:", "Nanga Parbat", "K2", "Tirich Mir", "Rakaposhi", "B", "Geography", "K2 is the highest mountain in Pakistan and the second-highest in the world."),
            Q("The Indus River flows primarily through:", "Pakistan", "Sri Lanka", "Nepal", "Bangladesh", "A", "Geography", "The Indus is the major river system of Pakistan."),
            Q("The national poet of Pakistan is commonly recognized as:", "Faiz Ahmed Faiz", "Allama Muhammad Iqbal", "Mir Taqi Mir", "Josh Malihabadi", "B", "Culture", "Allama Iqbal is widely recognized as Pakistan's national poet."),
            Q("Pakistan's national flower is:", "Rose", "Jasmine", "Sunflower", "Tulip", "B", "Culture", "Jasmine is Pakistan's national flower."),
            Q("Pakistan's national animal is:", "Markhor", "Lion", "Snow leopard", "Camel", "A", "Culture", "The markhor is Pakistan's national animal."),
            Q("The State Bank of Pakistan was established in:", "1947", "1948", "1956", "1973", "B", "Economy", "The State Bank of Pakistan began operations in 1948."),
            Q("The first capital of Pakistan after independence was:", "Lahore", "Karachi", "Islamabad", "Rawalpindi", "B", "History", "Karachi served as the first capital before the capital was moved to Rawalpindi temporarily and then Islamabad."),
            Q("Minar-e-Pakistan is located in:", "Karachi", "Lahore", "Islamabad", "Multan", "B", "History", "Minar-e-Pakistan stands in Iqbal Park, Lahore."),
            Q("Pakistan became a member of the United Nations in:", "1947", "1948", "1950", "1956", "A", "International Relations", "Pakistan joined the United Nations on 30 September 1947."),
            Q("The national sport traditionally associated with Pakistan is:", "Cricket", "Hockey", "Football", "Squash", "B", "Culture and Sports", "Field hockey is traditionally recognized as Pakistan's national sport." )
        ]),

        new("Computer Science", "Computer Science MCQs covering programming, databases, operating systems, networking, algorithms, security and web technology.",
        [
            Q("The brain of a computer is commonly called the:", "RAM", "CPU", "Hard disk", "Monitor", "B", "Computer Fundamentals", "The CPU executes instructions and performs core processing."),
            Q("RAM is best described as:", "Permanent read-only storage", "Volatile working memory", "An output device", "A network protocol", "B", "Computer Fundamentals", "RAM is volatile memory used for active programs and data."),
            Q("Which number system uses only 0 and 1?", "Decimal", "Binary", "Octal", "Hexadecimal", "B", "Number Systems", "Binary uses two digits: 0 and 1."),
            Q("HTML is primarily used to:", "Style databases", "Structure web pages", "Manage CPU registers", "Encrypt hard drives", "B", "Web Development", "HTML provides the structure and content of web pages."),
            Q("CSS is primarily used to:", "Style web pages", "Compile C# code", "Manage SQL transactions", "Store images in RAM", "A", "Web Development", "CSS controls presentation and layout of web documents."),
            Q("JavaScript is mainly used on the web to add:", "Only database tables", "Interactivity and dynamic behaviour", "Physical RAM", "Network cables", "B", "Web Development", "JavaScript enables client-side interactivity and dynamic behaviour."),
            Q("SQL is primarily used for:", "Managing relational databases", "Editing photographs", "Designing processors", "Compressing audio", "A", "Databases", "SQL is the standard language used to query and manage relational data."),
            Q("A primary key in a relational table should:", "Allow duplicate identifiers", "Uniquely identify each row", "Always contain text", "Always be a foreign key", "B", "Databases", "A primary key uniquely identifies a record."),
            Q("Which SQL clause filters rows before grouping?", "ORDER BY", "WHERE", "HAVING", "GROUP BY", "B", "Databases", "WHERE filters individual rows before GROUP BY."),
            Q("Which data structure follows LIFO order?", "Queue", "Stack", "Tree", "Graph", "B", "Data Structures", "A stack follows Last-In, First-Out."),
            Q("Which data structure follows FIFO order?", "Stack", "Queue", "Heap", "Tree", "B", "Data Structures", "A queue follows First-In, First-Out."),
            Q("Binary search requires the data to be:", "Completely random", "Sorted", "Encrypted", "Stored only in a database", "B", "Algorithms", "Binary search repeatedly halves the search space in sorted data."),
            Q("The average time complexity of binary search is:", "O(n²)", "O(n)", "O(log n)", "O(2ⁿ)", "C", "Algorithms", "Binary search runs in logarithmic time on a sorted array."),
            Q("Which protocol is commonly used to securely transfer web pages?", "HTTP", "HTTPS", "FTP only", "SMTP", "B", "Networking", "HTTPS is HTTP over TLS and protects web traffic in transit."),
            Q("An IP address is used to:", "Identify a device/interface on an IP network", "Increase monitor brightness", "Compile programs", "Store passwords only", "A", "Networking", "IP addresses identify network interfaces for routing and communication."),
            Q("DNS primarily translates:", "IP addresses to electricity", "Domain names to IP addresses", "HTML to CSS", "RAM to ROM", "B", "Networking", "DNS resolves human-readable domain names to IP addresses."),
            Q("Which operating system is open-source and Unix-like?", "Linux", "MS-DOS only", "iOS only", "BIOS", "A", "Operating Systems", "Linux is an open-source Unix-like operating system kernel and ecosystem."),
            Q("A firewall is primarily used to:", "Filter network traffic", "Increase CPU clock speed", "Create images", "Replace RAM", "A", "Cyber Security", "Firewalls control network traffic according to security rules."),
            Q("Hashing is commonly used in security to:", "Create a fixed-size digest from data", "Make a monitor brighter", "Increase Wi-Fi range physically", "Convert RAM to CPU", "A", "Cyber Security", "Cryptographic hash functions produce fixed-size digests used for integrity and other purposes."),
            Q("Which HTTP method is conventionally used to retrieve a resource?", "GET", "DELETE", "PATCH", "CONNECT only", "A", "Web Development", "GET requests retrieve a representation of a resource." )
        ]),

        new("Mathematics", "Mathematics MCQs covering arithmetic, algebra, geometry, trigonometry, probability, statistics and basic calculus.",
        [
            Q("What is 15 + 27?", "32", "42", "52", "62", "B", "Arithmetic", "15 + 27 = 42."),
            Q("What is 12 × 8?", "86", "96", "108", "112", "B", "Arithmetic", "12 multiplied by 8 equals 96."),
            Q("What is 144 ÷ 12?", "10", "11", "12", "14", "C", "Arithmetic", "144 divided by 12 equals 12."),
            Q("What is the square root of 81?", "7", "8", "9", "10", "C", "Arithmetic", "9 × 9 = 81."),
            Q("If x + 7 = 15, then x equals:", "6", "7", "8", "9", "C", "Algebra", "Subtract 7 from both sides: x = 8."),
            Q("If 2x = 18, then x equals:", "6", "8", "9", "12", "C", "Algebra", "Divide both sides by 2: x = 9."),
            Q("What is 25% of 200?", "25", "40", "50", "75", "C", "Percentages", "25% of 200 is 50."),
            Q("The value of 3² + 4² is:", "7", "12", "25", "49", "C", "Algebra", "9 + 16 = 25."),
            Q("The perimeter of a square with side 5 cm is:", "10 cm", "15 cm", "20 cm", "25 cm", "C", "Geometry", "Perimeter of a square is 4 × side = 20 cm."),
            Q("The area of a rectangle 8 cm long and 5 cm wide is:", "13 cm²", "26 cm²", "40 cm²", "80 cm²", "C", "Geometry", "Area = length × width = 8 × 5 = 40 cm²."),
            Q("The sum of the interior angles of a triangle is:", "90°", "180°", "270°", "360°", "B", "Geometry", "The interior angles of a triangle sum to 180°."),
            Q("A right angle measures:", "45°", "90°", "120°", "180°", "B", "Geometry", "A right angle is 90 degrees."),
            Q("sin 90° equals:", "0", "1", "-1", "1/2", "B", "Trigonometry", "The sine of 90° is 1."),
            Q("cos 0° equals:", "0", "1", "-1", "1/2", "B", "Trigonometry", "The cosine of 0° is 1."),
            Q("The probability of getting a head when a fair coin is tossed once is:", "0", "1/4", "1/2", "1", "C", "Probability", "A fair coin has two equally likely outcomes, so P(head) = 1/2."),
            Q("The mean of 2, 4 and 6 is:", "3", "4", "5", "6", "B", "Statistics", "Mean = (2 + 4 + 6) / 3 = 4."),
            Q("The median of 3, 7 and 9 is:", "3", "7", "8", "9", "B", "Statistics", "The middle value of the ordered set is 7."),
            Q("If a = 3 and b = 4, then a² + b² equals:", "7", "12", "25", "49", "C", "Algebra", "3² + 4² = 9 + 16 = 25."),
            Q("The derivative of x² with respect to x is:", "x", "2x", "x²", "2", "B", "Calculus", "By the power rule, d(x²)/dx = 2x."),
            Q("The value of 2⁵ is:", "10", "16", "32", "64", "C", "Exponents", "2 × 2 × 2 × 2 × 2 = 32." )
        ])
    ];

    private static SeedQuestion Q(
        string text,
        string a,
        string b,
        string c,
        string d,
        string answer,
        string topic,
        string explanation) =>
        new(text, a, b, c, d, answer, topic, explanation);
}
