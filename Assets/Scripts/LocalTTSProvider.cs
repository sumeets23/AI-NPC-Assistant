using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Free, offline TTS on Windows.
/// Writes a temp PowerShell script, runs it to produce a WAV file,
/// then parses the WAV bytes into a Unity AudioClip.
/// No extra DLL references needed — PowerShell already has System.Speech.
/// </summary>
public static class LocalTTSProvider
{
    /// <summary>
    /// Synthesises <paramref name="text"/> asynchronously.
    /// Returns a ready-to-play AudioClip or null on failure.
    /// </summary>
    public static async Task<AudioClip> SynthesiseAsync(string text, string voiceName = null, int rate = 0)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string tempDir = Application.temporaryCachePath;
        string uid     = Guid.NewGuid().ToString("N");
        string wavPath = Path.Combine(tempDir, $"npc_tts_{uid}.wav");
        string ps1Path = Path.Combine(tempDir, $"npc_tts_{uid}.ps1");

        try
        {
            // Write the speech text to a plain UTF-8 file so we never need to
            // escape it inside the PowerShell script (handles smart quotes,
            // apostrophes, em-dashes, and any other Unicode from the LLM).
            string txtPath = Path.Combine(tempDir, $"npc_tts_{uid}.txt");
            File.WriteAllText(txtPath, text, System.Text.Encoding.UTF8);

            string clampedRate = Mathf.Clamp(rate, -10, 10).ToString();

            string script =
                "Add-Type -AssemblyName System.Speech\r\n" +
                $"$text = Get-Content -Path \"{txtPath.Replace("\\", "\\\\")}\" -Raw -Encoding UTF8\r\n" +
                "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer\r\n" +
                (string.IsNullOrEmpty(voiceName) ? "" : $"try {{ $s.SelectVoice(\"{voiceName}\") }} catch {{}}\r\n") +
                $"$s.Rate = {clampedRate}\r\n" +
                $"$s.SetOutputToWaveFile(\"{wavPath.Replace("\\", "\\\\")}\")\r\n" +
                "$s.Speak($text)\r\n" +
                "$s.Dispose()\r\n";

            File.WriteAllText(ps1Path, script);

            // Run on background thread — never blocks Unity's main thread
            string error = await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "powershell.exe",
                    Arguments              = $"-NoProfile -NonInteractive -File \"{ps1Path}\"",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                };
                using var proc = Process.Start(psi);
                string stderr  = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30_000);
                return stderr;
            });

            // Cleanup temp script and text files
            try { File.Delete(ps1Path); } catch { }
            try { File.Delete(txtPath); } catch { }

            if (!string.IsNullOrWhiteSpace(error))
                UnityEngine.Debug.LogWarning($"[LocalTTSProvider] PowerShell stderr: {error}");

            if (!File.Exists(wavPath))
            {
                UnityEngine.Debug.LogError("[LocalTTSProvider] WAV file was not produced. Check PowerShell output.");
                return null;
            }

            byte[]    wavBytes = File.ReadAllBytes(wavPath);
            AudioClip clip     = ParseWavToAudioClip(wavBytes, "LocalTTS");

            try { File.Delete(wavPath); } catch { }

            return clip;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[LocalTTSProvider] Exception: {e.Message}");
            return null;
        }
#else
        UnityEngine.Debug.LogWarning("[LocalTTSProvider] Windows local TTS is only supported on Windows.");
        return await Task.FromResult<AudioClip>(null);
#endif
    }

    // ------------------------------------------------------------------
    // WAV → AudioClip
    // SpeechSynthesizer outputs standard 16-bit mono/stereo PCM.
    // ------------------------------------------------------------------
    private static AudioClip ParseWavToAudioClip(byte[] wav, string clipName)
    {
        if (wav == null || wav.Length < 44)
        {
            UnityEngine.Debug.LogError("[LocalTTSProvider] WAV buffer too small.");
            return null;
        }

        try
        {
            int channels      = BitConverter.ToInt16(wav, 22);
            int sampleRate    = BitConverter.ToInt32(wav, 24);
            int bitsPerSample = BitConverter.ToInt16(wav, 34);

            // Find "data" chunk
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string tag  = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                int    size = BitConverter.ToInt32(wav, pos + 4);
                if (tag == "data") { pos += 8; break; }
                pos += 8 + size;
            }

            int dataBytes      = wav.Length - pos;
            int bytesPerSample = bitsPerSample / 8;
            int totalSamples   = dataBytes / bytesPerSample;
            int samplesPerChan = totalSamples / channels;

            float[] samples = new float[totalSamples];

            if (bitsPerSample == 16)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short raw  = BitConverter.ToInt16(wav, pos + i * 2);
                    samples[i] = raw / 32768f;
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = 0; i < totalSamples; i++)
                    samples[i] = (wav[pos + i] - 128) / 128f;
            }
            else
            {
                UnityEngine.Debug.LogError($"[LocalTTSProvider] Unsupported WAV bit depth: {bitsPerSample}");
                return null;
            }

            var clip = AudioClip.Create(clipName, samplesPerChan, channels, sampleRate, false);
            clip.SetData(samples, 0);
            UnityEngine.Debug.Log($"[LocalTTSProvider] AudioClip created: {samplesPerChan} samples, {channels}ch, {sampleRate}Hz");
            return clip;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[LocalTTSProvider] WAV parse error: {e.Message}");
            return null;
        }
    }
}
