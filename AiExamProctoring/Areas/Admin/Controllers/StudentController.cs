using AiExamProctoring.Data;
using AiExamProctoring.Models;
using AiExamProctoring.Services.FaceIdentity;
using AiExamProctoring.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiExamProctoring.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class StudentController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StudentController> _logger;
        private readonly IFaceIdentityClient _faceIdentity;

        public StudentController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<StudentController> logger,
            IFaceIdentityClient faceIdentity)
        {
            _userManager = userManager;
            _context = context;
            _logger = logger;
            _faceIdentity = faceIdentity;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? department,
            string? batch,
            string? semester,
            bool? approved,
            bool? blocked)
        {
            try
            {
                var studentIds = (await _userManager.GetUsersInRoleAsync("Student"))
                    .Select(x => x.Id)
                    .ToHashSet();

                var query = _userManager.Users
                    .AsNoTracking()
                    .Where(x => studentIds.Contains(x.Id));

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();
                    query = query.Where(x =>
                        (x.FullName != null && x.FullName.Contains(search)) ||
                        (x.Email != null && x.Email.Contains(search)) ||
                        (x.CNIC != null && x.CNIC.Contains(search)) ||
                        (x.CandidateNumber != null && x.CandidateNumber.Contains(search)) ||
                        (x.RegistrationNumber != null && x.RegistrationNumber.Contains(search)));
                }

                if (!string.IsNullOrWhiteSpace(department))
                    query = query.Where(x => x.Department == department);

                if (!string.IsNullOrWhiteSpace(batch))
                    query = query.Where(x => x.Batch == batch);

                if (!string.IsNullOrWhiteSpace(semester))
                    query = query.Where(x => x.Semester == semester);

                if (approved.HasValue)
                    query = query.Where(x => x.IsApproved == approved.Value);

                if (blocked.HasValue)
                    query = query.Where(x => x.IsBlocked == blocked.Value);

                var students = await query
                    .OrderByDescending(x => x.CreatedDate)
                    .ToListAsync();

                // Keep filter dropdowns populated from the complete student set.
                var allStudents = await _userManager.Users
                    .AsNoTracking()
                    .Where(x => studentIds.Contains(x.Id))
                    .ToListAsync();

                ViewBag.Departments = allStudents.Where(x => !string.IsNullOrWhiteSpace(x.Department))
                    .Select(x => x.Department!).Distinct().OrderBy(x => x).ToList();
                ViewBag.Batches = allStudents.Where(x => !string.IsNullOrWhiteSpace(x.Batch))
                    .Select(x => x.Batch!).Distinct().OrderBy(x => x).ToList();
                ViewBag.Semesters = allStudents.Where(x => !string.IsNullOrWhiteSpace(x.Semester))
                    .Select(x => x.Semester!).Distinct().OrderBy(x => x).ToList();

                ViewBag.TotalStudents = students.Count;
                ViewBag.ApprovedCount = students.Count(x => x.IsApproved);
                ViewBag.BlockedCount = students.Count(x => x.IsBlocked);
                ViewBag.PendingCount = students.Count(x => !x.IsApproved);

                return View(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Student list error.");
                TempData["Error"] = "Unable to load students.";
                return View(new List<ApplicationUser>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            try
            {
                var student = await _userManager.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (student == null || !await IsStudent(id))
                    return NotFound();

                var attempts = await _context.ExamAttempts
                    .AsNoTracking()
                    .Include(x => x.Exam)
                    .Include(x => x.Result)
                    .Where(x => x.StudentId == id)
                    .OrderByDescending(x => x.StartedAt)
                    .ToListAsync();

                ViewBag.Attempts = attempts;
                ViewBag.TotalAttempts = attempts.Count;
                ViewBag.Completed = attempts.Count(x => x.Status == AttemptStatus.Completed);
                ViewBag.AutoSubmitted = attempts.Count(x => x.Status == AttemptStatus.AutoSubmitted);
                ViewBag.InProgress = attempts.Count(x =>
                    x.Status == AttemptStatus.InProgress ||
                    x.Status == AttemptStatus.ProctoringBlocked);
                ViewBag.Passed = attempts.Count(x => x.Result != null && x.Result.IsPassed);
                ViewBag.Failed = attempts.Count(x => x.Result != null && !x.Result.IsPassed);

                var scored = attempts.Where(x => x.Result != null).ToList();
                ViewBag.AverageScore = scored.Count == 0
                    ? 0
                    : Math.Round(scored.Average(x => (double)x.Result!.TotalMarksObtained), 2);

                ViewBag.AveragePercentage = scored.Count == 0
                    ? 0
                    : Math.Round(scored.Average(x => (double)x.Result!.Percentage), 2);

                var faceStatus = await _faceIdentity.TemplateStatusAsync(id, HttpContext.RequestAborted);
                ViewBag.FaceIdentityEnrolled = faceStatus.Success && faceStatus.Data?.Exists == true;
                ViewBag.FaceIdentityServiceReady = faceStatus.Success;
                ViewBag.FaceIdentityMessage = faceStatus.Success ? null : faceStatus.Message;

                return View(student);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Student details error. StudentId={StudentId}", id);
                TempData["Error"] = "Unable to load student details.";
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> ReviewPaper(int attemptId)
        {
            if (attemptId <= 0)
                return NotFound();

            try
            {
                var attempt = await _context.ExamAttempts
                    .AsNoTracking()

                    // Student
                    .Include(x => x.Student)

                    // Exam
                    .Include(x => x.Exam)

                    // Result
                    .Include(x => x.Result)

                    // Session Attempts
                    .Include(x => x.SessionAttempts)
                        .ThenInclude(x => x.Session)

                    // Answers -> Questions
                    .Include(x => x.SessionAttempts)
                        .ThenInclude(x => x.Answers)
                            .ThenInclude(x => x.Question)

                    .FirstOrDefaultAsync(x => x.AttemptId == attemptId);

                if (attempt == null)
                    return NotFound();

                // Security:
                // Admin sirf valid student attempt review kare.
                if (attempt.Student == null)
                    return NotFound();

                return View(attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Review paper error. AttemptId={AttemptId}",
                    attemptId);

                TempData["Error"] = "Unable to load examination paper.";

                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var student = await _userManager.FindByIdAsync(id);
            if (student == null || !await IsStudent(id)) return NotFound();

            return View(ToViewModel(student));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(StudentDashboardViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                var student = await _userManager.FindByIdAsync(model.Id);
                if (student == null || !await IsStudent(model.Id))
                    return NotFound();

                var email = model.Email?.Trim();
                if (string.IsNullOrWhiteSpace(email))
                {
                    ModelState.AddModelError(nameof(model.Email), "Email is required.");
                    return View(model);
                }

                if (!string.Equals(student.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    var existing = await _userManager.FindByEmailAsync(email);
                    if (existing != null && existing.Id != student.Id)
                    {
                        ModelState.AddModelError(nameof(model.Email), "Email already exists.");
                        return View(model);
                    }

                    var emailResult = await _userManager.SetEmailAsync(student, email);
                    if (!emailResult.Succeeded)
                    {
                        AddIdentityErrors(emailResult);
                        return View(model);
                    }

                    var usernameResult = await _userManager.SetUserNameAsync(student, email);
                    if (!usernameResult.Succeeded)
                    {
                        AddIdentityErrors(usernameResult);
                        return View(model);
                    }
                }

                student.FullName = model.FullName?.Trim() ?? string.Empty;
                student.PhoneNumber = model.PhoneNumber?.Trim();
                student.RollNumber = model.RollNumber?.Trim();
                student.RegistrationNumber = model.RegistrationNumber?.Trim();
                student.CNIC = model.CNIC?.Trim();
                student.FatherName = model.FatherName?.Trim();
                student.Gender = model.Gender;
                student.DateOfBirth = model.DateOfBirth;
                student.Department = model.Department?.Trim();
                student.Batch = model.Batch?.Trim();
                student.Semester = model.Semester?.Trim();
                student.Session = model.Session?.Trim();
                student.University = model.University?.Trim();
                student.Address = model.Address?.Trim();
                student.City = model.City?.Trim();
                student.Country = model.Country?.Trim();
                student.ProfileImage = model.ProfileImage?.Trim();
                student.HighestQualification = model.HighestQualification?.Trim();
                student.CurrentStatus = model.CurrentStatus;
                student.OrganizationName = model.OrganizationName?.Trim();
                student.IsApproved = model.IsApproved;
                student.IsBlocked = model.IsBlocked;

                var result = await _userManager.UpdateAsync(student);
                if (result.Succeeded && model.IsBlocked)
                    result = await _userManager.UpdateSecurityStampAsync(student);
                if (!result.Succeeded)
                {
                    AddIdentityErrors(result);
                    return View(model);
                }

                TempData["Success"] = "Student updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Edit student error. StudentId={StudentId}", model.Id);
                TempData["Error"] = "Unexpected error occurred.";
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string id) =>
            await SetStudentFlag(id, approved: true);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Block(string id) =>
            await SetStudentFlag(id, blocked: true);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unblock(string id) =>
            await SetStudentFlag(id, blocked: false);

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetFaceIdentity(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var student = await _userManager.FindByIdAsync(id);
            if (student == null || !await IsStudent(id)) return NotFound();
            var result = await _faceIdentity.DeleteTemplateAsync(id, HttpContext.RequestAborted);
            if (!result.Success)
                TempData["Error"] = result.Message;
            else
                TempData["Success"] = "Face identity template reset. The student can now complete a new multi-photo enrollment.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var student = await _userManager.FindByIdAsync(id);
            if (student == null || !await IsStudent(id)) return NotFound();

            ViewBag.HasAttempts = await _context.ExamAttempts.AnyAsync(x => x.StudentId == id);
            ViewBag.HasProctoringLogs = await _context.ProctoringLogs.AnyAsync(x => x.StudentId == id);

            return View(student);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            try
            {
                var student = await _userManager.FindByIdAsync(id);
                if (student == null || !await IsStudent(id))
                    return NotFound();

                var hasAttempts = await _context.ExamAttempts.AnyAsync(x => x.StudentId == id);
                var hasLogs = await _context.ProctoringLogs.AnyAsync(x => x.StudentId == id);

                if (hasAttempts || hasLogs)
                {
                    TempData["Error"] =
                        "Student cannot be deleted because exam/proctoring records exist.";
                    return RedirectToAction(nameof(Delete), new { id });
                }

                var result = await _userManager.DeleteAsync(student);
                if (!result.Succeeded)
                {
                    TempData["Error"] = string.Join(" | ",
                        result.Errors.Select(x => x.Description));
                    return RedirectToAction(nameof(Delete), new { id });
                }

                await _faceIdentity.DeleteTemplateAsync(id, HttpContext.RequestAborted);

                TempData["Success"] = "Student deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete student error. StudentId={StudentId}", id);
                TempData["Error"] = "Unexpected error occurred.";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task<bool> IsStudent(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            return user != null && await _userManager.IsInRoleAsync(user, "Student");
        }

        private StudentDashboardViewModel ToViewModel(ApplicationUser student) =>
            new()
            {
                Id = student.Id,
                FullName = student.FullName,
                Email = student.Email ?? string.Empty,
                PhoneNumber = student.PhoneNumber,
                RollNumber = student.RollNumber,
                RegistrationNumber = student.RegistrationNumber,
                CNIC = student.CNIC,
                FatherName = student.FatherName,
                Gender = student.Gender,
                DateOfBirth = student.DateOfBirth,
                Department = student.Department,
                Batch = student.Batch,
                Semester = student.Semester,
                Session = student.Session,
                University = student.University,
                Address = student.Address,
                City = student.City,
                Country = student.Country,
                ProfileImage = student.ProfileImage,
                HighestQualification = student.HighestQualification,
                CurrentStatus = student.CurrentStatus,
                OrganizationName = student.OrganizationName,
                IsApproved = student.IsApproved,
                IsBlocked = student.IsBlocked
            };

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
        }

        private async Task<IActionResult> SetStudentFlag(
            string id,
            bool? approved = null,
            bool? blocked = null)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            try
            {
                var student = await _userManager.FindByIdAsync(id);
                if (student == null || !await IsStudent(id))
                    return NotFound();

                if (approved.HasValue) student.IsApproved = approved.Value;
                if (blocked.HasValue) student.IsBlocked = blocked.Value;

                var result = await _userManager.UpdateAsync(student);
                if (result.Succeeded && blocked == true)
                    result = await _userManager.UpdateSecurityStampAsync(student);
                if (!result.Succeeded)
                {
                    TempData["Error"] = string.Join(" | ",
                        result.Errors.Select(x => x.Description));
                    return RedirectToAction(nameof(Index));
                }

                TempData["Success"] =
                    approved == true ? "Student approved successfully." :
                    blocked == true ? "Student blocked successfully." :
                    blocked == false ? "Student unblocked successfully." :
                    "Student updated successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Student flag update error. StudentId={StudentId}", id);
                TempData["Error"] = "Unable to update student.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
