using System.Data;
using System.Text.Json;
using AiExamProctoring.Models;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Data;

/// <summary>
/// Curated versioned Computer Science question bank.
/// The old subject rows are ARCHIVED on first installation, never deleted:
/// previously selected questions, answers, grades, and historical examinations remain intact.
/// On later startups the seed inserts only missing questions. No duplicate starter imports.
/// </summary>
public static class ComputerScienceQuestionBankSeed
{
    public const string SeedMarker = "ComputerBank:v1";
    public const string SeedFile = "ComputerScienceBank.v1.json";

    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger)
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Data", "Seeds", SeedFile);
        if (!File.Exists(seedPath))
            throw new FileNotFoundException("Computer Science question bank data was not copied to the build/publish output.", seedPath);

        var sources = JsonSerializer.Deserialize<List<BankSubject>>(
            await File.ReadAllTextAsync(seedPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Computer question-bank JSON is empty.");
        Validate(sources);

        // EF's SQL Server execution strategy requires the transaction to be created
        // INSIDE the retry delegate. Everything is committed atomically.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            var firstInstall = !await db.Questions.AsNoTracking()
                .AnyAsync(q => q.Tags != null && q.Tags.Contains(SeedMarker));
            var currentSubjects = await db.Subjects.ToListAsync();
            var names = sources.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (firstInstall)
            {
                var archived = 0;
                foreach (var subject in currentSubjects)
                {
                    if (!names.Contains(subject.SubjectName) && subject.IsActive)
                    {
                        subject.IsActive = false;
                        subject.UpdatedDate = DateTime.Now;
                        archived++;
                    }
                }
                // Do not delete or re-key Questions. ExamQuestions and StudentAnswers
                // may still refer to them; archived subjects are filtered from NEW papers.
                logger.LogInformation("Computer bank first installation: archived {Count} legacy subject folders without deleting exam history.", archived);
            }

            for (var subjectNumber = 0; subjectNumber < sources.Count; subjectNumber++)
            {
                var source = sources[subjectNumber];
                var subject = currentSubjects.FirstOrDefault(s =>
                    string.Equals(s.SubjectName, source.Name, StringComparison.OrdinalIgnoreCase));
                if (subject is null)
                {
                    subject = new Subject
                    {
                        SubjectName = source.Name, Description = source.Description,
                        IsActive = true, IsLocked = false, CreatedDate = DateTime.Now
                    };
                    db.Subjects.Add(subject);
                    currentSubjects.Add(subject);
                    await db.SaveChangesAsync();
                }
                else
                {
                    if (!subject.IsActive) { subject.IsActive = true; subject.UpdatedDate = DateTime.Now; }
                    // Preserve manually configured subject descriptions.
                }

                var existing = (await db.Questions.AsNoTracking()
                    .Where(q => q.SubjectId == subject.SubjectId)
                    .Select(q => q.QuestionText)
                    .ToListAsync())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var order = await db.Questions.AsNoTracking()
                    .Where(q => q.SubjectId == subject.SubjectId)
                    .Select(q => (int?)q.DisplayOrder).MaxAsync() ?? 0;
                var newQuestions = new List<Question>();

                for (var i = 0; i < source.Questions.Count; i++)
                {
                    var item = source.Questions[i];
                    if (!existing.Add(item.Text)) continue;
                    var rawOptions = new[] { item.Correct, item.Wrong1, item.Wrong2, item.Wrong3 };
                    var options = new string[4];
                    var correctIndex = (i + subjectNumber) % 4;
                    for (var j = 0; j < 4; j++)
                        options[(correctIndex + j) % 4] = rawOptions[j];
                    newQuestions.Add(new Question
                    {
                        SubjectId = subject.SubjectId,
                        QuestionText = item.Text,
                        OptionA = options[0], OptionB = options[1],
                        OptionC = options[2], OptionD = options[3],
                        OptionE = null,
                        CorrectAnswer = ((char)('A' + correctIndex)).ToString(),
                        Topic = item.Topic,
                        Tags = $"{SeedMarker},Difficulty:{item.Difficulty}",
                        Explanation = item.Explanation,
                        DisplayOrder = ++order,
                        IsActive = true, IsVerified = true, IsLocked = false,
                        CreatedBy = "System Seed", CreatedDate = DateTime.Now
                    });
                }
                if (newQuestions.Count > 0) db.Questions.AddRange(newQuestions);
                await db.SaveChangesAsync();
                logger.LogInformation("Computer bank subject {Subject}: {Inserted} new questions out of {Expected}.",
                    source.Name, newQuestions.Count, source.Questions.Count);
            }
            if (tx is not null) await tx.CommitAsync();
        });
    }

    private static void Validate(List<BankSubject> sources)
    {
        if (sources.Count != 4 || sources.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new InvalidDataException("The Computer Science question bank must contain four unique subjects.");
        foreach (var subject in sources)
        {
            if (string.IsNullOrWhiteSpace(subject.Name) || subject.Name.Length > 150 ||
                subject.Description.Length > 500 || subject.Questions is null || subject.Questions.Count != 120)
                throw new InvalidDataException($"Invalid subject or count: {subject.Name}");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in subject.Questions)
            {
                var options = new[] { item.Correct, item.Wrong1, item.Wrong2, item.Wrong3 };
                if (string.IsNullOrWhiteSpace(item.Text) || item.Text.Length is < 10 or > 3000 ||
                    !seen.Add(item.Text) || string.IsNullOrWhiteSpace(item.Explanation) ||
                    item.Explanation.Length > 3000 || string.IsNullOrWhiteSpace(item.Topic) ||
                    item.Topic.Length > 150 || item.Difficulty is not ("Medium" or "Difficult") ||
                    options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 1000) ||
                    options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
                    throw new InvalidDataException($"Invalid MCQ/answer in subject {subject.Name}: {item.Text}");
            }
        }
    }

    public sealed record BankSubject(string Name, string Description, List<BankQuestion> Questions);
    public sealed record BankQuestion(string Topic, string Difficulty, string Text,
        string Correct, string Wrong1, string Wrong2, string Wrong3, string Explanation);
}
