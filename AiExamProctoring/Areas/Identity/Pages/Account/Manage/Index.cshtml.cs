
using AiExamProctoring.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AiExamProctoring.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public IndexModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public string Username { get; set; } = string.Empty;

        [TempData]
        public string? StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            [Required]
            [Display(Name = "Full Name")]
            [StringLength(100)]
            public string FullName { get; set; } = string.Empty;

            [Required]
            [Phone]
            [Display(Name = "Phone Number")]
            public string PhoneNumber { get; set; } = string.Empty;

            [Display(Name = "Roll Number")]
            public string? RollNumber { get; set; }

            [Display(Name = "Department")]
            public string? Department { get; set; }

            [Display(Name = "Batch")]
            public string? Batch { get; set; }

            [Display(Name = "Semester")]
            public string? Semester { get; set; }

            public bool IsApproved { get; set; }

            public bool IsBlocked { get; set; }

            public string? ProfileImage { get; set; }
        }

        private async Task LoadAsync(ApplicationUser user)
        {
            Username = await _userManager.GetUserNameAsync(user) ?? "";

            Input = new InputModel
            {
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber ?? "",
                RollNumber = user.RollNumber,
                Department = user.Department,
                Batch = user.Batch,
                Semester = user.Semester,
                IsApproved = user.IsApproved,
                IsBlocked = user.IsBlocked,
                ProfileImage = user.ProfileImage
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            bool updated = false;

            if (user.FullName != Input.FullName)
            {
                user.FullName = Input.FullName;
                updated = true;
            }

            if (user.PhoneNumber != Input.PhoneNumber)
            {
                user.PhoneNumber = Input.PhoneNumber;
                updated = true;
            }

            if (user.RollNumber != Input.RollNumber)
            {
                user.RollNumber = Input.RollNumber;
                updated = true;
            }

            if (user.Department != Input.Department)
            {
                user.Department = Input.Department;
                updated = true;
            }

            if (user.Batch != Input.Batch)
            {
                user.Batch = Input.Batch;
                updated = true;
            }

            if (user.Semester != Input.Semester)
            {
                user.Semester = Input.Semester;
                updated = true;
            }

            if (updated)
            {
                var result = await _userManager.UpdateAsync(user);

                if (!result.Succeeded)
                {
                    StatusMessage = "Error updating profile.";

                    await LoadAsync(user);

                    return Page();
                }
            }

            await _signInManager.RefreshSignInAsync(user);

            StatusMessage = "Your profile has been updated successfully.";

            return RedirectToPage();
        }
    }
}