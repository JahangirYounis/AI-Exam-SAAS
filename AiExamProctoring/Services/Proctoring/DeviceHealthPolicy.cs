namespace AiExamProctoring.Services.Proctoring;
public sealed record DeviceHealthEpisode(string Issue, DateTime Started, DateTime Seen, int Warnings);
public sealed record DeviceHealthDecision(DeviceHealthEpisode? Episode, bool Warn, bool Hold);
public static class DeviceHealthPolicy {
 public static DeviceHealthDecision Sample(DeviceHealthEpisode? old, string issue, DateTime now) {
  if (issue == "healthy") return new(null, false, false);
  if (old == null || old.Issue != issue || now < old.Seen || now - old.Seen > TimeSpan.FromSeconds(8)) old = new(issue,now,now,0);
  var elapsed = (now-old.Started).TotalSeconds;
  var count = elapsed >= 14 ? 3 : elapsed >= 7 ? 2 : 1;
  return new(old with { Seen=now, Warnings=count }, count > old.Warnings, elapsed >= 20);
 }
}
public sealed record DeviceHealthReply(bool Warning, string Issue, int WarningCount, bool NeedsReview, ProctoringState State);
