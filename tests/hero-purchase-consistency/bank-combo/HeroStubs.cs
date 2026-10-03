// Boundary stand-in for the single transitive helper HeroRecruitmentContexts calls out of the
// full hero recruitment class (that class and its whole slice are not part of this
// gate-combination suite; both gate sources under test are linked as production).
internal static class HeroRecruitment
{
    internal static bool IsCharacterRecord(IslandSaveData.ObjectData data)
    {
        if (data?.componentData2 == null) return false;
        foreach (var component in data.componentData2)
            if (component != null && component.name == "Character" && component.type == "CharacterData") return true;
        return false;
    }
}
