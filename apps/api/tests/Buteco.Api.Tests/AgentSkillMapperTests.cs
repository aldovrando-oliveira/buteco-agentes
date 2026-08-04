using Buteco.Api.A2A;
using Buteco.Api.Agents.Entities;

namespace Buteco.Api.Tests;

public class AgentSkillMapperTests
{
    [Fact]
    public void Slugify_SimpleName_ProducesLowercaseHyphenatedSlug()
    {
        Assert.Equal("consulta-cep", AgentSkillMapper.Slugify("Consulta CEP"));
    }

    [Fact]
    public void Slugify_NameWithAccentsAndSpecialCharacters_StripsDiacriticsAndSymbols()
    {
        Assert.Equal("atendimento-ao-cliente", AgentSkillMapper.Slugify("Atendimento ao Cliente!"));
        Assert.Equal("consulta-cep", AgentSkillMapper.Slugify("Consulta CÉP"));
        Assert.Equal("agendamento-consulta", AgentSkillMapper.Slugify("Agendamento & Consulta"));
    }

    [Fact]
    public void Slugify_NameWithoutAlphanumericCharacters_FallsBackToSkill()
    {
        Assert.Equal("skill", AgentSkillMapper.Slugify("!!!"));
    }

    [Fact]
    public void MapSkills_EmptyList_ReturnsEmptyList()
    {
        var result = AgentSkillMapper.MapSkills([]);

        Assert.Empty(result);
    }

    [Fact]
    public void MapSkills_SkillWithDescription_MapsNameAndDescription()
    {
        var result = AgentSkillMapper.MapSkills([new Skill("Consulta CEP", "Consulta endereço a partir do CEP.")]);

        var mapped = Assert.Single(result);
        Assert.Equal("Consulta CEP", mapped.Name);
        Assert.Equal("Consulta endereço a partir do CEP.", mapped.Description);
        Assert.Equal("consulta-cep", mapped.Id);
        Assert.Empty(mapped.Tags);
    }

    [Fact]
    public void MapSkills_SkillWithoutDescription_MapsEmptyDescription()
    {
        var result = AgentSkillMapper.MapSkills([new Skill("Consulta CEP", null)]);

        var mapped = Assert.Single(result);
        Assert.Equal(string.Empty, mapped.Description);
    }

    [Fact]
    public void MapSkills_CollidingNames_ProduceDistinctDeterministicIds()
    {
        var skills = new List<Skill>
        {
            new("Consulta CEP", null),
            new("Consulta CEP", "Segunda variante"),
            new("Consulta CEP", "Terceira variante"),
        };

        var first = AgentSkillMapper.MapSkills(skills);
        var second = AgentSkillMapper.MapSkills(skills);

        Assert.Equal(["consulta-cep", "consulta-cep-2", "consulta-cep-3"], first.Select(s => s.Id));
        Assert.Equal(first.Select(s => s.Id), second.Select(s => s.Id));
    }
}
