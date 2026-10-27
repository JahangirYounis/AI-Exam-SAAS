namespace AiExamProctoring.Services.FaceIdentity;

public static class IdentityMessage
{
    public static string For(string? reason, string fallback) => reason switch
    {
        "poor_lighting" => "Your face is too dark to check. Face a light and try again.",
        "blurry_face" or "low_face_quality" => "The camera picture is not clear. Clean the camera, hold still and try again.",
        "presentation_unconfirmed" => "We could not check your live face. Face the camera, improve the light and try again.",
        "borderline_score" => "We could not confirm your identity yet. Look at the camera and try again.",
        "identity_mismatch" => "Identity mismatch. Your face does not match the registered student.",
        "no_face" => "No face is visible. Please sit in front of the camera.",
        "multiple_faces" => "More than one face is visible. Only the student should be in the camera view.",
        "face_too_small" => "Your face is too far away. Move closer to the camera.",
        _ => fallback
    };
}
