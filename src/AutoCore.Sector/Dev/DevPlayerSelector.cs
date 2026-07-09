namespace AutoCore.Sector.Dev;

public static class DevPlayerSelector
{
    public static DevConnectedCharacter Select(IReadOnlyList<DevConnectedCharacter> characters, string characterName)
    {
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            var matches = characters
                .Where(c => string.Equals(c.CharacterName, characterName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException($"No connected character named '{characterName}' was found."),
                _ => throw new InvalidOperationException($"Multiple connected characters named '{characterName}' were found.")
            };
        }

        return characters.Count switch
        {
            1 => characters[0],
            0 => throw new InvalidOperationException("No connected characters were found."),
            _ => throw new InvalidOperationException("Multiple connected characters were found. Specify a character name.")
        };
    }
}
