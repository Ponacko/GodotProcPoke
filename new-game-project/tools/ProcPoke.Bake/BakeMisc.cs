using ProcPoke.Data;

namespace ProcPoke.Bake;

internal sealed partial class Baker
{
    private List<EvolutionRule> BakeEvolutions()
    {
        var evo = Table("pokemon_evolution.csv");
        var evolvesFrom = SpeciesEvolvesFrom();
        var result = new List<EvolutionRule>();

        // The evolution table is version-versioned: each evolved species may have a row per version group
        // (e.g. Leafeon evolves near a Moss Rock through Gen 7, by Leaf Stone from Gen 8). Pick, per species,
        // the rows at the most recent version group at or before B2W2 — the method that was live in Gen 5.
        foreach (var group in evo.Rows.GroupBy(r => evo.Int(r, "evolved_species_id")))
        {
            var to = group.Key;
            if (to > DataPin.MaxSpeciesId) continue;
            if (!evolvesFrom.TryGetValue(to, out var from) || from is null || from > DataPin.MaxSpeciesId) continue;

            var atOrBefore = group
                .Where(r => _vgOrder.TryGetValue(evo.Int(r, "version_group_id"), out var o) && o <= DataPin.VersionGroupOrder)
                .ToList();
            List<string[]> chosen = atOrBefore.Count > 0
                ? [.. atOrBefore.Where(r => evo.Int(r, "version_group_id") == BestVersionGroup(evo, atOrBefore))]
                : [.. group.Where(r => !evo.HasColumn("is_default") || evo.Bool(r, "is_default"))];

            foreach (var row in chosen)
                result.Add(BuildEvolution(evo, from.Value, to, row));
        }

        result.Sort((a, b) => a.ToSpeciesId != b.ToSpeciesId
            ? a.ToSpeciesId.CompareTo(b.ToSpeciesId)
            : a.FromSpeciesId.CompareTo(b.FromSpeciesId));
        return result;
    }

    /// <summary>The version group with the highest order among candidate rows (most recent ≤ B2W2).</summary>
    private int BestVersionGroup(CsvTable evo, List<string[]> rows)
        => rows.Select(r => evo.Int(r, "version_group_id")).MaxBy(vg => _vgOrder[vg]);

    private EvolutionRule BuildEvolution(CsvTable evo, int from, int to, string[] row)
    {
            var triggerId = evo.Int(row, "evolution_trigger_id");
            var wasTrade = triggerId == 2;
            var trigger = wasTrade ? EvolutionTrigger.LinkCable : MapTrigger(triggerId);

            string? tod = evo.Str(row, "time_of_day");

            return new EvolutionRule
            {
                FromSpeciesId = from,
                ToSpeciesId = to,
                Trigger = trigger,
                WasTrade = wasTrade,
                MinLevel = evo.IntOrNull(row, "minimum_level"),
                TriggerItemId = evo.IntOrNull(row, "trigger_item_id"),
                HeldItemId = evo.IntOrNull(row, "held_item_id"),
                TimeOfDay = string.IsNullOrEmpty(tod) ? null : tod,
                KnownMoveId = evo.IntOrNull(row, "known_move_id"),
                KnownMoveTypeId = evo.IntOrNull(row, "known_move_type_id"),
                MinHappiness = evo.IntOrNull(row, "minimum_happiness"),
                MinBeauty = evo.IntOrNull(row, "minimum_beauty"),
                GenderId = evo.IntOrNull(row, "gender_id"),
                RelativePhysicalStats = evo.IntOrNull(row, "relative_physical_stats"),
                PartySpeciesId = evo.IntOrNull(row, "party_species_id"),
                PartyTypeId = evo.IntOrNull(row, "party_type_id"),
                LocationId = evo.IntOrNull(row, "location_id"),
                NeedsSpecialRock = evo.HasColumn("near_special_rock") && evo.Bool(row, "near_special_rock"),
                NeedsOverworldRain = evo.Bool(row, "needs_overworld_rain"),
                TurnUpsideDown = evo.Bool(row, "turn_upside_down"),
            };
    }

    private static EvolutionTrigger MapTrigger(int id) => id switch
    {
        1 => EvolutionTrigger.LevelUp,
        3 => EvolutionTrigger.UseItem,
        4 => EvolutionTrigger.Shed,
        _ => EvolutionTrigger.Other,
    };

    private Dictionary<int, int?> SpeciesEvolvesFrom()
    {
        var t = Table("pokemon_species.csv");
        return t.Rows.ToDictionary(r => t.Int(r, "id"), r => t.IntOrNull(r, "evolves_from_species_id"));
    }

    /// <summary>Item categories worth baking at MVP (balls, medicine, machines, evolution &amp; held items).</summary>
    private static readonly HashSet<string> ItemCategoryWhitelist = new(StringComparer.Ordinal)
    {
        "standard-balls", "special-balls",
        "healing", "status-cures", "revival", "pp-recovery", "vitamins",
        "all-machines",
        "evolution", "spelunking",
        "held-items", "choice", "effort-training", "type-enhancement",
        "species-specific", "training", "bad-held-items", "scarves", "in-a-pinch",
    };

    private List<Item> BakeItems()
    {
        var items = Table("items.csv");
        var categories = Table("item_categories.csv");
        var names = EnglishNames("item_names.csv", "item_id");

        var categoryName = categories.Rows.ToDictionary(r => categories.Int(r, "id"), r => categories.Str(r, "identifier"));

        // Items any baked evolution references must be present regardless of category.
        var evo = Table("pokemon_evolution.csv");
        var referenced = new HashSet<int>();
        foreach (var r in evo.Rows)
        {
            if (evo.IntOrNull(r, "trigger_item_id") is int ti) referenced.Add(ti);
            if (evo.IntOrNull(r, "held_item_id") is int hi) referenced.Add(hi);
        }

        var result = new List<Item>();
        foreach (var row in items.Rows)
        {
            var id = items.Int(row, "id");
            var category = categoryName.GetValueOrDefault(items.Int(row, "category_id"), "");
            if (!ItemCategoryWhitelist.Contains(category) && !referenced.Contains(id)) continue;

            result.Add(new Item
            {
                Id = id,
                Name = names.GetValueOrDefault(id, TitleCase(items.Str(row, "identifier"))),
                Category = category,
                Cost = items.Int(row, "cost"),
                FlingPower = items.IntOrNull(row, "fling_power"),
            });
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    private List<Nature> BakeNatures()
    {
        var natures = Table("natures.csv");
        var names = EnglishNames("nature_names.csv", "nature_id");
        var result = new List<Nature>();

        foreach (var row in natures.Rows)
        {
            var id = natures.Int(row, "id");
            var dec = natures.Int(row, "decreased_stat_id");
            var inc = natures.Int(row, "increased_stat_id");
            var neutral = dec == inc;

            result.Add(new Nature
            {
                Id = id,
                Name = names.GetValueOrDefault(id, TitleCase(natures.Str(row, "identifier"))),
                IncreasedStat = neutral ? null : MapStat(inc),
                DecreasedStat = neutral ? null : MapStat(dec),
            });
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    private List<GrowthRate> BakeGrowthRates()
    {
        var rates = Table("growth_rates.csv");
        var experience = Table("experience.csv");

        var byRate = GroupBy(experience, "growth_rate_id");
        var result = new List<GrowthRate>();

        foreach (var row in rates.Rows)
        {
            var id = rates.Int(row, "id");
            var curve = new int[101];
            foreach (var e in byRate.GetValueOrDefault(id, []))
            {
                var level = experience.Int(e, "level");
                if (level is >= 1 and <= 100) curve[level] = experience.Int(e, "experience");
            }
            result.Add(new GrowthRate { Name = rates.Str(row, "identifier"), CumulativeExperience = curve });
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private List<string> BakeNameBlocklist()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);

        var locations = Table("location_names.csv");
        foreach (var row in locations.Rows)
        {
            if (locations.Int(row, "local_language_id") != EnglishLang) continue;
            var name = locations.Str(row, "name");
            if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
            if (locations.HasColumn("subtitle"))
            {
                var sub = locations.Str(row, "subtitle");
                if (!string.IsNullOrWhiteSpace(sub)) set.Add(sub);
            }
        }

        var regions = Table("region_names.csv");
        foreach (var row in regions.Rows)
        {
            if (regions.Int(row, "local_language_id") != EnglishLang) continue;
            var name = regions.Str(row, "name");
            if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
        }

        return set.ToList();
    }
}
