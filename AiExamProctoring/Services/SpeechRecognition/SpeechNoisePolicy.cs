
namespace AiExamProctoring.Services.SpeechRecognition;

/// <summary>Only an acoustic level heuristic. It cannot identify the source of a sound.</summary>
public static class SpeechNoisePolicy
{
    // A high RMS while VAD reports no speech is an environment-quality observation,
    // never evidence of candidate speaking or a cheating strike.
    public static double? Pcm16WavRms(ReadOnlySpan<byte>
    bytes)
    {
        if (bytes.Length < 48 || bytes.Length > 12_000_000 ||
        !bytes[..4].SequenceEqual("RIFF"u8) ||
        !bytes.Slice(8, 4).SequenceEqual("WAVE"u8)) return null;
        // The exam AudioWorklet emits 44-byte mono PCM16, 16 kHz WAV. Reject
        // arbitrary containers rather than interpreting non-audio header bytes.
        if (!bytes.Slice(12, 4).SequenceEqual("fmt "u8) ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(20, 2)) != 1 ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(22, 2)) != 1 ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(24, 4)) != 16000 ||
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(34, 2)) != 16 ||
        !bytes.Slice(36, 4).SequenceEqual("data"u8)) return null;
        var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(40, 4));
        if (size < 3200 || size > bytes.Length - 44 || (size & 1) != 0) return null;
        double sum = 0;
        var samples = (int)size / 2;
        for (var i = 0; i < samples; i++)
        {
            var sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(44 + i * 2, 2)) / 32768.0;
            sum += sample * sample;
        }
        return Math.Sqrt(sum / samples);
    }

    // Uncalibrated acoustic warning threshold. Field calibration is required.
    public static bool ElevatedUnknownNoise(double? rms, bool vadDetectedSpeech) =>
    !vadDetectedSpeech && rms is double value && value >= 0.11 && value <= 1;
}