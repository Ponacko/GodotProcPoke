using System;
using Godot;
using ProcPoke.Overworld;

namespace ProcPoke.OverworldView;

/// <summary>Thin Godot seam that captures identity before the overworld session is started.</summary>
public partial class NewGameIdentityController : Node
{
    public PlayerIdentity? Identity { get; private set; }

    public event Action<PlayerIdentity>? Started;

    public bool IsStarted => Identity is not null;

    public bool Begin(CharacterChoice character, string name, out string? error)
    {
        try
        {
            Identity = NewGameIdentity.Create(character, name);
            error = null;
            Started?.Invoke(Identity);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
