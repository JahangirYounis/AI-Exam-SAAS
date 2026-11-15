using System.Text.Json;
namespace AiExamProctoring.Services.Proctoring;
public sealed class ExamObjectPolicy {
 public string Version {get;set;}="object-policy-v1";
 // Safe defaults: the candidate's main computer, screen, water and writing tools
  // are not automatically forbidden. Teachers may explicitly configure rules.
  public List<string> Prohibited {get;set;}=new(ProctoringObjectPolicy.DefaultProhibited);
 public double ConfirmationSeconds {get;set;}=1.5;
 public int ConfirmationSamples {get;set;}=3;
 public double RecoverySeconds {get;set;}=3;
 public Dictionary<string,decimal> Thresholds {get;set;}=new();
}
public static class ProctoringObjectPolicy {
 public const double MinimumConfirmationSeconds = .5;
 public static bool CanConfirmAutomatically(string label) => label.ToLowerInvariant() is not ("laptop" or "monitor" or "tv");
  public static readonly string[] DefaultProhibited = { "mobile_phone", "book", "tablet", "earphones", "smart_watch", "calculator", "notes_paper" };
 public static readonly Dictionary<string,string> Names=new(){["mobile_phone"]="Mobile Phone",["book"]="Book",["laptop"]="Laptop",["earphones"]="Earbuds",["monitor"]="Display",["tablet"]="Tablet",["smart_watch"]="Wrist Device",["calculator"]="Calculator",["notes_paper"]="Paper or Notes",["pen_pencil"]="Pen or Pencil",["bag"]="Bag",["bottle"]="Water Bottle"};
 // Compatibility for older callers; this is not the current examination policy.
 public static readonly HashSet<string> BannedLabels=new(new[]{"mobile_phone","tablet","earphones"},StringComparer.OrdinalIgnoreCase);
 public static ExamObjectPolicy Read(string? json){
  ExamObjectPolicy p;try{p=string.IsNullOrWhiteSpace(json)?new():JsonSerializer.Deserialize<ExamObjectPolicy>(json)??new();}catch(JsonException){p=new();}
  p.Prohibited=(p.Prohibited??new()).Where(Names.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
  p.ConfirmationSeconds=double.IsFinite(p.ConfirmationSeconds)?Math.Clamp(p.ConfirmationSeconds,MinimumConfirmationSeconds,10):1.5;
  p.ConfirmationSamples=Math.Clamp(p.ConfirmationSamples,2,10);p.RecoverySeconds=double.IsFinite(p.RecoverySeconds)?Math.Clamp(p.RecoverySeconds,2,15):3;
  p.Thresholds??=new();return p;
 }
 public static decimal Threshold(ExamObjectPolicy p,string label)=>p.Thresholds.TryGetValue(label,out var value)?Math.Clamp(value,.35m,.99m):label switch{"smart_watch" or "earphones"=>.75m,"notes_paper"=>.7m,"mobile_phone"=>.6m,_=>.65m};
 public static string Title(string label)=>Names.TryGetValue(label,out var name)?$"{name} Detected":"Visual Observation — Review Required";
}

