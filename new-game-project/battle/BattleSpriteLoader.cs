using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace ProcPoke.BattleView;

/// <summary>
/// Loads a species' battle sprite as a Godot <see cref="SpriteFrames"/> animation from the bake tool's
/// frame-split output (<c>.cache/assets/battle/{id}[-back]/frame_NN.png</c> + <c>frames.json</c> — see
/// <c>ProcPoke.Bake</c>'s <c>Assets.cs</c>). Falls back from back to front, and from front to a plain
/// placeholder square, so a species whose sprite hasn't been fetched yet never crashes the scene.
/// </summary>
public static class BattleSpriteLoader
{
    private const string AnimationName = "idle";
    private const double BaseFrameMs = 100.0;

    /// <summary><paramref name="IsBackArt"/> tells the caller whether a real back sprite was found, so it
    /// can flip only the front-art fallback (a true back sprite is different art, not a mirrored front).</summary>
    public readonly record struct SpriteLoadResult(SpriteFrames Frames, bool IsBackArt);

    public static SpriteLoadResult Load(int speciesId, bool preferBack)
    {
        if (preferBack && TryLoadFromDisk(speciesId, back: true, out var backFrames))
            return new SpriteLoadResult(backFrames, IsBackArt: true);
        if (TryLoadFromDisk(speciesId, back: false, out var frontFrames))
            return new SpriteLoadResult(frontFrames, IsBackArt: false);
        return new SpriteLoadResult(Placeholder(), IsBackArt: false);
    }

    public static SpriteFrames Placeholder()
    {
        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        frames.AddAnimation(AnimationName);
        var image = Image.CreateEmpty(48, 48, false, Image.Format.Rgba8);
        image.Fill(new Color(0.55f, 0.55f, 0.6f));
        frames.AddFrame(AnimationName, ImageTexture.CreateFromImage(image));
        return frames;
    }

    private static bool TryLoadFromDisk(int speciesId, bool back, out SpriteFrames frames)
    {
        frames = null!;
        var dir = ProjectSettings.GlobalizePath($"res://.cache/assets/battle/{speciesId}{(back ? "-back" : "")}");
        var manifestPath = Path.Combine(dir, "frames.json");
        if (!File.Exists(manifestPath)) return false;

        var manifest = JsonSerializer.Deserialize<FramesManifest>(File.ReadAllText(manifestPath));
        if (manifest is null || manifest.FrameCount <= 0) return false;

        var built = new SpriteFrames();
        built.RemoveAnimation("default");
        built.AddAnimation(AnimationName);
        // 4.6.2's SpriteFrames API predates SetAnimationLoopMode (4.7+); this older overload still works.
        built.SetAnimationLoop(AnimationName, true);
        built.SetAnimationSpeed(AnimationName, (float)(1000.0 / BaseFrameMs));
        for (var i = 0; i < manifest.FrameCount; i++)
        {
            var image = new Image();
            if (image.Load(Path.Combine(dir, $"frame_{i:00}.png")) != Error.Ok) continue;
            var delay = i < manifest.DelaysMs.Count ? manifest.DelaysMs[i] : (int)BaseFrameMs;
            built.AddFrame(AnimationName, ImageTexture.CreateFromImage(image), (float)(delay / BaseFrameMs));
        }
        frames = built;
        return true;
    }

    private sealed class FramesManifest
    {
        public int FrameCount { get; set; }
        public List<int> DelaysMs { get; set; } = [];
    }
}
