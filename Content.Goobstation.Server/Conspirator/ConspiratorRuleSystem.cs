// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Server.Antag;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.PDA.Ringer;
using Content.Server.Roles;
using Content.Server.Traitor.Uplink;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.NPC.Systems;
using Content.Shared.PDA;
using Content.Shared.Random.Helpers;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Roles.RoleCodeword;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using System.Text;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Goobstation.Server.Conspirator.Objectives;

namespace Content.Goobstation.Server.Conspirator;

public sealed class ConspiratorRuleSystem : GameRuleSystem<ConspiratorRuleComponent> 
{
    private static readonly Color ConspiratorCodewordColor = Color.FromHex("#cc3b3b"); // Same scheme as traitor

    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly SharedRoleCodewordSystem _roleCodewordSystem = default!;
    [Dependency] private readonly SharedRoleSystem _roleSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ConspiratorRuleComponent, AfterAntagEntitySelectedEvent>(AfterEntitySelected);
        SubscribeLocalEvent<ConspiratorRuleComponent, ObjectivesTextPrependEvent>(OnObjectivesTextPrepend);
    }

    protected override void Added (EntityUid uid, ConspiratorRuleComponent component, GameRuleComponent gamerule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gamerule, args);
        SetCodewords(component, args.RuleEntity);
    }

    private void AfterEntitySelected(Entity<ConspiratorRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        Log.Debug($"AfterAntagEntitySelected {ToPrettyString(ent)}");
        MakeConspirator(args.EntityUid, ent);
    }

    private void SetCodewords(TraitorRuleComponent component, EntityUid ruleEntity)
    {
        component.Codewords = GenerateConspiratorCodewords(component);
        _adminLogger.Add(LogType.EventStarted, LogImpact.Low, $"Codewords generated for game rule {ToPrettyString(ruleEntity)}: {string.Join(", ", component.Codewords)}");
    }

    public string[] GenerateConspiratorCodewords(TraitorRuleComponent component)
    {
        var adjectives = _prototypeManager.Index(component.CodewordAdjectives).Values;
        var verbs = _prototypeManager.Index(component.CodewordVerbs).Values;
        var codewordPool = adjectives.Concat(verbs).ToList();
        var finalCodewordCount = Math.Min(component.CodewordCount, codewordPool.Count);
        string[] codewords = new string[finalCodewordCount];
        for (var i = 0; i < finalCodewordCount; i++)
        {
            codewords[i] = Loc.GetString(_random.PickAndTake(codewordPool));
        }
        return codewords;
    }

    public bool MakeConspirator(EntityUid conspirator, ConspiratorRuleComponent component)
    {
        //Grab mind if not provided
        if(!_mindSystem.TryGetMind(conspirator, out var mindId, out var mind))
            return false;

        var briefing = "";

        if (component.GiveCodewords) 
        {
            Log.Debug($"MakeConspirator {ToPrettyString(conspirator)} - added codewords flufftext to briefing");
            briefing = Loc.GetString("conspirator-role-codewords-short", ("codewords", string.Join(", ", component.Codewords)));
        }

        var issuer = _random.Pick(_prototypeManager.Index(component.ObjectiveIssuers)); // Yoink that from traitor

        _antag.SendBriefing(conspirator, GenerateBriefing(component.Codewords, issuer). Color.Crimson, component.GreetSoundNotification);

        component.ConspiratorMinds.Add(mindId);

        if (_roleSystem.MindHasRole<ConspiratorRoleComponent>(mindId, out var conspiratorRole))
        {
            AddComp<RoleBriefingComponent>(conspiratorRole.Value.Owner);
            component<RoleBriefingComponent>(conspiratorRole.Value.Owner).Briefing = GenerateBriefingCharacter(component.Codewords, issuer);
        }

        // Send codewords to only the Conspirator client
        var color = ConspiratorCodewordColor; // Fall back to a dark red Syndicate color if a prototype is not found

        RoleCodewordComponent codewordComp = EnsureComp<RoleCodewordComponent>(mindId);
        _roleCodewordSystem.SetRoleCodewords(codewordComp, "conspirator", component.Codewords.ToList(), color);

        // Change the faction
        _npcFaction.RemoveFaction(conspirator, component.NanoTrasenFaction, false);
        _npcFaction.AddFaction(conspirator, component.SyndicateFaction);

        return true;
    }
        private void OnObjectivesTextPrepend(EntityUid uid, TraitorRuleComponent comp, ref ObjectivesTextPrependEvent args)
    {
        if(comp.GiveCodewords)
            args.Text += "\n" + Loc.GetString("traitor-round-end-codewords", ("codewords", string.Join(", ", comp.Codewords))); // Todo: Adjust round end to separate conspirators and traitors
    }
    private string GenerateBriefing(string[] codewords, string objectiveIssuer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\n" + Loc.GetString($"conspirator-{objectiveIssuer}-intro"));

        sb.AppendLine("\n" + Loc.GetString($"conspirator-role-nouplink"));

        sb.AppendLine("\n" + Loc.GetString($"conspirator-role-codewords", ("codewords", string.Join(", ", codewords))));

        sb.AppendLine("\n" + Loc.GetString($"conspirator-role-moreinfo"));

        return sb.ToString();
    }
}