using Godot;
using System;

namespace VoxelSiege.Core;

/// <summary>
/// Generates retro-style 8-bit sound effects procedurally at startup.
/// No external audio files needed — all sounds are synthesized from waveforms.
/// Registers generated AudioStreamWav instances with AudioDirector.
/// </summary>
public partial class RetroSFXGenerator : Node
{
    private const int SampleRate = 22050;
    private const int BitsPerSample = 16;

    private int _retryCount;

    public override void _Ready()
    {
        _retryCount = 0;
        // Wait one frame so AudioDirector is ready
        CallDeferred(nameof(GenerateAndRegister));
    }

    private void GenerateAndRegister()
    {
        AudioDirector? audio = AudioDirector.Instance;
        if (audio == null)
        {
            _retryCount++;
            if (_retryCount < 10)
            {
                GD.Print($"[RetroSFX] AudioDirector not ready yet, retrying ({_retryCount})...");
                CallDeferred(nameof(GenerateAndRegister));
                return;
            }
            GD.PrintErr("[RetroSFX] AudioDirector not found after retries.");
            return;
        }

        if (!audio.IsInsideTree() || !audio.IsNodeReady())
        {
            _retryCount++;
            if (_retryCount < 10)
            {
                GD.Print($"[RetroSFX] AudioDirector not in tree yet, retrying ({_retryCount})...");
                CallDeferred(nameof(GenerateAndRegister));
                return;
            }
            GD.PrintErr("[RetroSFX] AudioDirector never became ready.");
            return;
        }

        // Block interactions
        audio.RegisterSFX("block_place", GenerateBlockPlace());
        audio.RegisterSFX("block_remove", GenerateBlockRemove());

        // Explosions / weapons
        audio.RegisterSFX("cannon_fire", GenerateCannonFire());
        audio.RegisterSFX("mortar_fire", GenerateMortarFire());
        audio.RegisterSFX("railgun_fire", GenerateRailgunFire());
        audio.RegisterSFX("missile_fire", GenerateMissileFire());
        audio.RegisterSFX("drill_fire", GenerateDrillFire());
        audio.RegisterSFX("weapon_fire", GenerateCannonFire());

        // Explosion impact
        audio.RegisterSFX("explosion_impact", GenerateExplosionImpact());

        // Impact / damage
        audio.RegisterSFX("commander_hit", GenerateCommanderHit());
        audio.RegisterSFX("commander_death", GenerateCommanderDeath());
        audio.RegisterSFX("debris_impact", GenerateDebrisPing());

        // Phase transitions
        audio.RegisterSFX("fog_reveal", GenerateFogReveal());
        audio.RegisterSFX("game_over", GenerateGameOver());

        // Combat countdown
        audio.RegisterSFX("countdown_tick", GenerateCountdownTick());
        audio.RegisterSFX("countdown_fight", GenerateCountdownFight());

        // Backup bombardment alert
        audio.RegisterSFX("backup_alert", GenerateBackupAlert());

        GD.Print("[RetroSFX] All retro SFX generated and registered.");
    }

    // ─── Block place: short low "thunk" ───
    private AudioStreamWav GenerateBlockPlace()
    {
        int samples = (int)(SampleRate * 0.08f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 15f);
            float freq = 180f - t * 800f;
            float val = MathF.Sign(MathF.Sin(2f * MathF.PI * freq * t)) * env * 0.5f;
            WriteSample(data, i, val);
        }
        return CreateWav(data);
    }

    // ─── Block remove: higher pitch "chip" ───
    private AudioStreamWav GenerateBlockRemove()
    {
        int samples = (int)(SampleRate * 0.06f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 20f);
            float freq = 400f + t * 600f;
            float val = MathF.Sign(MathF.Sin(2f * MathF.PI * freq * t)) * env * 0.4f;
            WriteSample(data, i, val);
        }
        return CreateWav(data);
    }

    // ─── Cannon fire: bassy boom with noise tail ───
    private AudioStreamWav GenerateCannonFire()
    {
        int samples = (int)(SampleRate * 0.35f);
        byte[] data = new byte[samples * 2];
        Random rng = new Random(1);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 3.5f);
            float boom = MathF.Sin(2f * MathF.PI * (60f - t * 40f) * t) * env * 0.6f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * env * env * 0.4f;
            float crackle = MathF.Sin(2f * MathF.PI * 120f * t) * MathF.Max(0f, 1f - t * 8f) * 0.3f;
            WriteSample(data, i, Clamp(boom + noise + crackle));
        }
        return CreateWav(data);
    }

    // ─── Mortar fire: "thoomp" with rising pitch ───
    private AudioStreamWav GenerateMortarFire()
    {
        int samples = (int)(SampleRate * 0.25f);
        byte[] data = new byte[samples * 2];
        Random rng = new Random(2);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 5f);
            float freq = 80f + t * 200f;
            float tone = MathF.Sin(2f * MathF.PI * freq * t) * env * 0.5f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * MathF.Max(0f, 1f - t * 10f) * 0.3f;
            WriteSample(data, i, Clamp(tone + noise));
        }
        return CreateWav(data);
    }

    // ─── Railgun fire: high-pitched "zap" with resonance ───
    private AudioStreamWav GenerateRailgunFire()
    {
        int samples = (int)(SampleRate * 0.2f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 6f);
            float freq1 = 800f + MathF.Sin(t * 40f) * 200f;
            float freq2 = 1200f - t * 500f;
            float val = (MathF.Sin(2f * MathF.PI * freq1 * t) * 0.4f +
                         MathF.Sin(2f * MathF.PI * freq2 * t) * 0.3f) * env;
            WriteSample(data, i, Clamp(val));
        }
        return CreateWav(data);
    }

    // ─── Missile fire: whoosh with rising tone ───
    private AudioStreamWav GenerateMissileFire()
    {
        int samples = (int)(SampleRate * 0.3f);
        byte[] data = new byte[samples * 2];
        Random rng = new Random(3);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Min(t * 20f, 1f) * MathF.Max(0f, 1f - t * 4f);
            float freq = 200f + t * 800f;
            float tone = MathF.Sin(2f * MathF.PI * freq * t) * 0.3f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.4f;
            WriteSample(data, i, Clamp((tone + noise) * env));
        }
        return CreateWav(data);
    }

    // ─── Drill fire: grinding buzz ───
    private AudioStreamWav GenerateDrillFire()
    {
        int samples = (int)(SampleRate * 0.4f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Min(t * 10f, 1f) * MathF.Max(0f, 1f - t * 3f);
            float buzz = MathF.Sign(MathF.Sin(2f * MathF.PI * 60f * t)) * 0.3f;
            float grind = MathF.Sin(2f * MathF.PI * (300f + MathF.Sin(t * 80f) * 100f) * t) * 0.3f;
            WriteSample(data, i, Clamp((buzz + grind) * env));
        }
        return CreateWav(data);
    }

    // ─── Explosion impact: deep boom with rumble and crackle ───
    private AudioStreamWav GenerateExplosionImpact()
    {
        int samples = (int)(SampleRate * 0.5f);
        byte[] data = new byte[samples * 2];
        Random rng = new Random(6);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 2.5f);
            // Deep bass boom with pitch drop
            float boom = MathF.Sin(2f * MathF.PI * (50f - t * 30f) * t) * env * 0.6f;
            // Sub-bass rumble
            float rumble = MathF.Sin(2f * MathF.PI * 35f * t) * env * env * 0.3f;
            // Noise crackle that fades quickly
            float crackle = ((float)rng.NextDouble() * 2f - 1f) * MathF.Max(0f, 1f - t * 5f) * 0.4f;
            // Mid-frequency impact body
            float body = MathF.Sin(2f * MathF.PI * 100f * t) * MathF.Max(0f, 1f - t * 6f) * 0.3f;
            WriteSample(data, i, Clamp(boom + rumble + crackle + body));
        }
        return CreateWav(data);
    }

    // ─── Commander hit: impact thud ───
    private AudioStreamWav GenerateCommanderHit()
    {
        int samples = (int)(SampleRate * 0.15f);
        byte[] data = new byte[samples * 2];
        Random rng = new Random(4);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 8f);
            float hit = MathF.Sin(2f * MathF.PI * 150f * t) * env * 0.5f;
            float crack = ((float)rng.NextDouble() * 2f - 1f) * MathF.Max(0f, 1f - t * 20f) * 0.3f;
            WriteSample(data, i, Clamp(hit + crack));
        }
        return CreateWav(data);
    }

    // ─── Commander death: dramatic descending tone + explosion ───
    private AudioStreamWav GenerateCommanderDeath()
    {
        int samples = (int)(SampleRate * 0.8f);
        byte[] data = new byte[samples * 2];
        Random rng = new Random(5);
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 1.5f);
            float freq = 600f - t * 500f;
            float tone = MathF.Sin(2f * MathF.PI * freq * t) * 0.4f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * MathF.Max(0f, 0.8f - t) * 0.3f;
            float rumble = MathF.Sin(2f * MathF.PI * 40f * t) * env * 0.3f;
            WriteSample(data, i, Clamp((tone + noise + rumble) * env));
        }
        return CreateWav(data);
    }

    // ─── Debris ping: short metallic "tink" ───
    private AudioStreamWav GenerateDebrisPing()
    {
        int samples = (int)(SampleRate * 0.05f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 25f);
            float freq = 2000f + MathF.Sin(t * 100f) * 500f;
            float val = MathF.Sin(2f * MathF.PI * freq * t) * env * 0.25f;
            WriteSample(data, i, val);
        }
        return CreateWav(data);
    }

    // ─── Fog reveal: sweeping atmospheric rise ───
    private AudioStreamWav GenerateFogReveal()
    {
        int samples = (int)(SampleRate * 0.6f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Min(t * 4f, 1f) * MathF.Max(0f, 1f - (t - 0.4f) * 5f);
            float freq = 200f + t * 400f;
            float val = (MathF.Sin(2f * MathF.PI * freq * t) * 0.3f +
                         MathF.Sin(2f * MathF.PI * freq * 1.5f * t) * 0.2f) * env;
            WriteSample(data, i, Clamp(val));
        }
        return CreateWav(data);
    }

    // ─── Game over: descending arpeggio ───
    private AudioStreamWav GenerateGameOver()
    {
        int samples = (int)(SampleRate * 1.0f);
        byte[] data = new byte[samples * 2];
        float[] notes = { 523f, 440f, 349f, 262f }; // C5, A4, F4, C4 descending
        float noteDuration = 0.2f;
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            int noteIndex = Math.Min((int)(t / noteDuration), notes.Length - 1);
            float noteT = t - noteIndex * noteDuration;
            float env = MathF.Max(0f, 1f - noteT * 6f);
            float freq = notes[noteIndex];
            // Square wave for retro feel
            float val = MathF.Sign(MathF.Sin(2f * MathF.PI * freq * t)) * env * 0.35f;
            // Overall fadeout
            val *= MathF.Max(0f, 1f - t * 1.2f);
            WriteSample(data, i, Clamp(val));
        }
        return CreateWav(data);
    }

    // ─── Countdown tick: short percussive beep (plays for 3, 2, 1) ───
    private AudioStreamWav GenerateCountdownTick()
    {
        int samples = (int)(SampleRate * 0.15f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Max(0f, 1f - t * 8f);
            // Clean square wave at a mid-high pitch for a retro countdown feel
            float freq = 440f;
            float val = MathF.Sign(MathF.Sin(2f * MathF.PI * freq * t)) * env * 0.45f;
            WriteSample(data, i, Clamp(val));
        }
        return CreateWav(data);
    }

    // ─── Countdown fight: triumphant rising chord (plays for "FIGHT!") ───
    private AudioStreamWav GenerateCountdownFight()
    {
        int samples = (int)(SampleRate * 0.4f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Min(t * 12f, 1f) * MathF.Max(0f, 1f - t * 2.5f);
            // Major chord: root, major third, fifth
            float tone1 = MathF.Sign(MathF.Sin(2f * MathF.PI * 523f * t)) * 0.25f; // C5
            float tone2 = MathF.Sign(MathF.Sin(2f * MathF.PI * 659f * t)) * 0.2f;  // E5
            float tone3 = MathF.Sign(MathF.Sin(2f * MathF.PI * 784f * t)) * 0.2f;  // G5
            float val = (tone1 + tone2 + tone3) * env;
            WriteSample(data, i, Clamp(val));
        }
        return CreateWav(data);
    }

    // ─── Backup alert: urgent two-tone siren burst ───
    private AudioStreamWav GenerateBackupAlert()
    {
        int samples = (int)(SampleRate * 0.6f);
        byte[] data = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)SampleRate;
            float env = MathF.Min(t * 8f, 1f) * MathF.Max(0f, 1f - t * 1.5f);
            // Two-tone siren: alternates between high and low every 0.1s
            float sirenFreq = (MathF.Floor(t * 10f) % 2 == 0) ? 880f : 660f;
            float tone = MathF.Sign(MathF.Sin(2f * MathF.PI * sirenFreq * t)) * 0.3f;
            // Add a sub-bass rumble for urgency
            float bass = MathF.Sin(2f * MathF.PI * 80f * t) * 0.2f * env;
            float val = (tone + bass) * env;
            WriteSample(data, i, Clamp(val));
        }
        return CreateWav(data);
    }

    // ─── Helpers ───

    private static void WriteSample(byte[] buffer, int sampleIndex, float value)
    {
        short pcm = (short)(Mathf.Clamp(value, -1f, 1f) * 32767f);
        int byteIndex = sampleIndex * 2;
        buffer[byteIndex] = (byte)(pcm & 0xFF);
        buffer[byteIndex + 1] = (byte)((pcm >> 8) & 0xFF);
    }

    private static float Clamp(float v) => MathF.Max(-1f, MathF.Min(1f, v));

    private static AudioStreamWav CreateWav(byte[] pcmData)
    {
        AudioStreamWav wav = new AudioStreamWav();
        wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
        wav.MixRate = SampleRate;
        wav.Stereo = false;
        wav.Data = pcmData;
        return wav;
    }
}
