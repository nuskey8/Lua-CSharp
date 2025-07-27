using Lua.Standard;

namespace Lua.Tests;

public class PatternMatchingTests
{
    [Test]
    public async Task Test_StringMatch_BasicPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Literal match
        var result = await state.DoStringAsync("return string.match('hello world', 'hello')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));

        result = await state.DoStringAsync("return string.match('hello world', 'world')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("world"));

        // No match
        result = await state.DoStringAsync("return string.match('hello world', 'xyz')");
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));
    }

    [Test]
    public async Task Test_StringMatch_CharacterClasses()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // %d - digits
        var result = await state.DoStringAsync("return string.match('hello123', '%d')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("1"));

        result = await state.DoStringAsync("return string.match('hello123', '%d+')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("123"));

        // %a - letters
        result = await state.DoStringAsync("return string.match('123hello', '%a+')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));

        // %w - alphanumeric
        result = await state.DoStringAsync("return string.match('test_123', '%w+')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("test"));

        // %s - whitespace
        result = await state.DoStringAsync("return string.match('hello world', '%s')");
        Assert.That(result[0].Read<string>(), Is.EqualTo(" "));
    }

    [Test]
    public async Task Test_StringMatch_Quantifiers()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // + (one or more)
        var result = await state.DoStringAsync("return string.match('aaa', 'a+')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("aaa"));

        // * (zero or more)
        result = await state.DoStringAsync("return string.match('bbb', 'a*b')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("b"));

        result = await state.DoStringAsync("return string.match('aaab', 'a*b')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("aaab"));

        // ? (optional)
        result = await state.DoStringAsync("return string.match('color', 'colou?r')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("color"));

        result = await state.DoStringAsync("return string.match('colour', 'colou?r')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("colour"));

        // - (minimal repetition)
        result = await state.DoStringAsync("return string.match('aaab', 'a-b')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("aaab"));
    }

    [Test]
    public async Task Test_StringMatch_Captures()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Single capture
        var result = await state.DoStringAsync("return string.match('hello world', '(%a+)')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));

        // Multiple captures
        result = await state.DoStringAsync("return string.match('hello world', '(%a+) (%a+)')");
        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("world"));

        // Position capture
        result = await state.DoStringAsync("return string.match('hello', '()llo')");
        Assert.That(result[0].Read<double>(), Is.EqualTo(3));

        // Email pattern
        result = await state.DoStringAsync("return string.match('test@example.com', '(%w+)@(%w+)%.(%w+)')");
        Assert.That(result, Has.Length.EqualTo(3));
        Assert.That(result[0].Read<string>(), Is.EqualTo("test"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("example"));
        Assert.That(result[2].Read<string>(), Is.EqualTo("com"));
    }

    [Test]
    public async Task Test_StringMatch_Anchors()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // ^ (start anchor)
        var result = await state.DoStringAsync("return string.match('hello world', '^hello')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));

        result = await state.DoStringAsync("return string.match('hello world', '^world')");
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));

        // $ (end anchor)
        result = await state.DoStringAsync("return string.match('hello world', 'world$')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("world"));

        result = await state.DoStringAsync("return string.match('hello world', 'hello$')");
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));
    }

    [Test]
    public async Task Test_StringMatch_WithInitPosition()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Start from specific position
        var result = await state.DoStringAsync("return string.match('hello world', 'o', 5)");
        Assert.That(result[0].Read<string>(), Is.EqualTo("o"));

        result = await state.DoStringAsync("return string.match('hello world', 'o', 8)");
        Assert.That(result[0].Read<string>(), Is.EqualTo("o"));

        // Negative init (from end)
        result = await state.DoStringAsync("return string.match('hello', 'l', -2)");
        Assert.That(result[0].Read<string>(), Is.EqualTo("l"));
    }

    [Test]
    public async Task Test_StringMatch_SpecialPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Dot (any character)
        var result = await state.DoStringAsync("return string.match('hello', 'h.llo')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));

        // Character sets
        result = await state.DoStringAsync("return string.match('hello123', '[0-9]+')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("123"));

        result = await state.DoStringAsync("return string.match('Hello', '[Hh]ello')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("Hello"));

        // Negated character sets
        result = await state.DoStringAsync("return string.match('hello123', '[^a-z]+')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("123"));
    }

    [Test]
    public async Task Test_StringFind_BasicUsage()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Basic literal search
        var result = await state.DoStringAsync("return string.find('hello world', 'world')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(7)); // Start position (1-based)
        Assert.That(result[1].Read<double>(), Is.EqualTo(11)); // End position (1-based)

        // Search with start position
        result = await state.DoStringAsync("return string.find('hello hello', 'hello', 3)");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(7)); // Second occurrence
        Assert.That(result[1].Read<double>(), Is.EqualTo(11));

        // No match
        result = await state.DoStringAsync("return string.find('hello world', 'xyz')");
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));
    }

    [Test]
    public async Task Test_StringFind_WithPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Pattern with captures
        var result = await state.DoStringAsync("return string.find('hello 123', '(%a+) (%d+)')");
        Assert.That(result.Length, Is.EqualTo(4)); // start, end, capture1, capture2
        Assert.That(result[0].Read<double>(), Is.EqualTo(1)); // Start position
        Assert.That(result[1].Read<double>(), Is.EqualTo(9)); // End position
        Assert.That(result[2].Read<string>(), Is.EqualTo("hello")); // First capture
        Assert.That(result[3].Read<string>(), Is.EqualTo("123")); // Second capture

        // Character class patterns
        result = await state.DoStringAsync("return string.find('abc123def', '%d+')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(4)); // Position of '123'
        Assert.That(result[1].Read<double>(), Is.EqualTo(6));

        // Anchored patterns
        result = await state.DoStringAsync("return string.find('hello world', '^hello')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(1));
        Assert.That(result[1].Read<double>(), Is.EqualTo(5));

        result = await state.DoStringAsync("return string.find('hello world', '^world')");
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));
    }

    [Test]
    public async Task Test_StringFind_PlainSearch()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Plain search (4th parameter = true)
        var result = await state.DoStringAsync("return string.find('hello (world)', '(world)', 1, true)");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(7)); // Start of '(world)'
        Assert.That(result[1].Read<double>(), Is.EqualTo(13)); // End of '(world)'

        // Pattern search would fail but plain search succeeds
        result = await state.DoStringAsync("return string.find('test%d+test', '%d+', 1, true)");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(5)); // Literal '%d+'
        Assert.That(result[1].Read<double>(), Is.EqualTo(7));
    }

    [Test]
    public async Task Test_StringFind_EdgeCases()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Empty pattern
        var result = await state.DoStringAsync("return string.find('hello', '')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(1));
        Assert.That(result[1].Read<double>(), Is.EqualTo(0));

        // Empty pattern with empty string (should match at position 1)
        result = await state.DoStringAsync("return string.find('', '')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(1));
        Assert.That(result[1].Read<double>(), Is.EqualTo(0));

        // Negative start position
        result = await state.DoStringAsync("return string.find('hello', 'l', -2)");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(4)); // Last 'l'
        Assert.That(result[1].Read<double>(), Is.EqualTo(4));

        // Start position beyond string length
        result = await state.DoStringAsync("return string.find('hello', 'l', 10)");
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));

        // Empty string with init beyond length
        result = await state.DoStringAsync("return string.find('', '', 2)");
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Type, Is.EqualTo(LuaValueType.Nil));

        // Position captures
        result = await state.DoStringAsync("return string.find('hello', '()l()l()')");
        Assert.That(result.Length, Is.EqualTo(5)); // start, end, pos1, pos2, pos3
        Assert.That(result[0].Read<double>(), Is.EqualTo(3)); // Start of match
        Assert.That(result[1].Read<double>(), Is.EqualTo(4)); // End of match
        Assert.That(result[2].Read<double>(), Is.EqualTo(3)); // Position before first 'l'
        Assert.That(result[3].Read<double>(), Is.EqualTo(4)); // Position before second 'l'
        Assert.That(result[4].Read<double>(), Is.EqualTo(5)); // Position after second 'l'
    }

    [Test]
    public async Task Test_StringGMatch_BasicUsage()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        // Test basic gmatch iteration
        var result = await state.DoStringAsync(@"
            local words = {}
            for word in string.gmatch('hello world lua', '%a+') do
                table.insert(words, word)
            end
            return table.unpack(words)
        ");
        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("world"));
        Assert.That(result[2].Read<string>(), Is.EqualTo("lua"));
    }

    [Test]
    public async Task Test_StringGMatch_WithCaptures()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test gmatch with captures
        var result = await state.DoStringAsync(@"
            local pairs = {}
            for key, value in string.gmatch('a=1 b=2 c=3', '(%a)=(%d)') do
                table.insert(pairs, key .. ':' .. value)
            end
            return table.unpack(pairs)
        ");
        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(result[0].Read<string>(), Is.EqualTo("a:1"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("b:2"));
        Assert.That(result[2].Read<string>(), Is.EqualTo("c:3"));
    }

    [Test]
    public async Task Test_StringGMatch_Numbers()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Extract all numbers from a string
        var result = await state.DoStringAsync(@"
            local numbers = {}
            for num in string.gmatch('price: $12.50, tax: $2.75, total: $15.25', '%d+%.%d+') do
                table.insert(numbers, num)
            end
            return table.unpack(numbers)
        ");
        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(result[0].Read<string>(), Is.EqualTo("12.50"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("2.75"));
        Assert.That(result[2].Read<string>(), Is.EqualTo("15.25"));
    }

    [Test]
    public async Task Test_StringGMatch_EmptyMatches()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test with pattern that can match empty strings
        var result = await state.DoStringAsync(@"
            local count = 0
            for match in string.gmatch('abc', 'a*') do
                count = count + 1
                if count > 10 then break end -- Prevent infinite loop
            end
            return count
        ");
        Assert.That(result[0].Read<double>(), Is.EqualTo(3));
    }

    [Test]
    public async Task Test_StringGMatch_ComplexPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Extract email-like patterns
        var result = await state.DoStringAsync(@"
            local emails = {}
            local text = 'Contact us at info@example.com or support@test.org for help'
            for email in string.gmatch(text, '%w+@%w+%.%w+') do
                table.insert(emails, email)
            end
            return table.unpack(emails)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("info@example.com"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("support@test.org"));
    }

    [Test]
    public async Task Test_StringGMatch_PositionCaptures()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test position captures with gmatch
        var result = await state.DoStringAsync(@"
            local positions = {}
            for pos, char in string.gmatch('hello', '()(%a)') do
                table.insert(positions, pos .. ':' .. char)
            end
            return table.unpack(positions)
        ");
        Assert.That(result.Length, Is.EqualTo(5));
        Assert.That(result[0].Read<string>(), Is.EqualTo("1:h"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("2:e"));
        Assert.That(result[2].Read<string>(), Is.EqualTo("3:l"));
        Assert.That(result[3].Read<string>(), Is.EqualTo("4:l"));
        Assert.That(result[4].Read<string>(), Is.EqualTo("5:o"));
    }

    [Test]
    public async Task Test_StringGMatch_NoMatches()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test when no matches are found
        var result = await state.DoStringAsync(@"
            local count = 0
            for match in string.gmatch('hello world', '%d+') do
                count = count + 1
            end
            return count
        ");
        Assert.That(result[0].Read<double>(), Is.EqualTo(0));
    }

    [Test]
    public async Task Test_StringGMatch_SingleCharacter()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test matching single characters
        var result = await state.DoStringAsync(@"
            local chars = {}
            for char in string.gmatch('a1b2c3', '%a') do
                table.insert(chars, char)
            end
            return table.unpack(chars)
        ");
        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(result[0].Read<string>(), Is.EqualTo("a"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("b"));
        Assert.That(result[2].Read<string>(), Is.EqualTo("c"));
    }

    [Test]
    public async Task Test_StringFind_And_GMatch_Consistency()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Test that find and gmatch work consistently with the same pattern
        var result = await state.DoStringAsync(@"
            local text = 'The quick brown fox jumps over the lazy dog'
            
            -- Find first word
            local start, end_pos, word1 = string.find(text, '(%a+)')
            
            -- Get first word from gmatch
            local word2 = string.gmatch(text, '%a+')()
            
            return word1, word2, start, end_pos
        ");
        Assert.That(result.Length, Is.EqualTo(4));
        Assert.That(result[0].Read<string>(), Is.EqualTo("The")); // From find
        Assert.That(result[1].Read<string>(), Is.EqualTo("The")); // From gmatch
        Assert.That(result[2].Read<double>(), Is.EqualTo(1)); // Start position
        Assert.That(result[3].Read<double>(), Is.EqualTo(3)); // End position
    }

    [Test]
    public async Task Test_Pattern_NegatedCharacterClassWithCapture()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Test the problematic pattern ^([^:]*):
        var result = await state.DoStringAsync(@"
            local text = 'key:value'
            local match = string.match(text, '^([^:]*):')
            return match
        ");

        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Read<string>(), Is.EqualTo("key"));

        // Test with empty match
        result = await state.DoStringAsync(@"
            local text = ':value'
            local match = string.match(text, '^([^:]*):')
            return match
        ");

        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Read<string>(), Is.EqualTo("")); // Empty string

        // Test with multiple captures
        result = await state.DoStringAsync(@"
            local text = '[key]:[value]:extra'
            local a, b = string.match(text, '^([^:]*):([^:]*)')
            return a, b
        ");

        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("[key]"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("[value]"));
    }

    [Test]
    public async Task Test_StringGSub_BasicReplacements()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Simple string replacement
        var result = await state.DoStringAsync("return string.gsub('hello world', 'world', 'lua')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello lua"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(1)); // Replacement count

        // Multiple replacements
        result = await state.DoStringAsync("return string.gsub('hello hello hello', 'hello', 'hi')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hi hi hi"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(3));

        // Limited replacements
        result = await state.DoStringAsync("return string.gsub('hello hello hello', 'hello', 'hi', 2)");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hi hi hello"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));
    }

    [Test]
    public async Task Test_StringGSub_PatternReplacements()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Character class patterns
        var result = await state.DoStringAsync("return string.gsub('hello123world456', '%d+', 'X')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("helloXworldX"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // Capture replacements
        result = await state.DoStringAsync("return string.gsub('John Doe', '(%a+) (%a+)', '%2, %1')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("Doe, John"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(1));

        // Whole match replacement (%0)
        result = await state.DoStringAsync("return string.gsub('test123', '%d+', '[%0]')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("test[123]"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(1));
    }

    [Test]
    public async Task Test_StringGSub_FunctionReplacements()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Function replacement
        var result = await state.DoStringAsync(@"
            return string.gsub('hello world', '%a+', function(s)
                return s:upper()
            end)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("HELLO WORLD"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // Function with position captures
        result = await state.DoStringAsync(@"
            return string.gsub('hello', '()l', function(pos)
                return '[' .. pos .. ']'
            end)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("he[3][4]o"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // Function returning nil (no replacement)
        result = await state.DoStringAsync(@"
            return string.gsub('a1b2c3', '%d', function(s)
                if s == '2' then return nil end
                return 'X'
            end)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("aXb2cX"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(3)); // Only 2 replacements made
    }

    [Test]
    public async Task Test_StringGSub_TableReplacements()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Table replacement
        var result = await state.DoStringAsync(@"
            local map = {hello = 'hi', world = 'lua'}
            return string.gsub('hello world', '%a+', map)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hi lua"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // Table with missing keys (no replacement)
        result = await state.DoStringAsync(@"
            local map = {hello = 'hi'}
            return string.gsub('hello world', '%a+', map)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hi world"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2)); // Only 'hello' was replaced
    }

    [Test]
    public async Task Test_StringGSub_EmptyPattern()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Empty pattern should match at every position
        var result = await state.DoStringAsync("return string.gsub('abc', '', '.')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo(".a.b.c."));
        Assert.That(result[1].Read<double>(), Is.EqualTo(4)); // 4 positions: before a, before b, before c, after c
    }

    [Test]
    public async Task Test_StringGSub_BalancedPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Balanced parentheses pattern
        var result = await state.DoStringAsync(@"
            return string.gsub('(hello) and (world)', '%b()', function(s)
                return s:upper()
            end)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("(HELLO) and (WORLD)"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // Balanced brackets
        result = await state.DoStringAsync("return string.gsub('[a][b][c]', '%b[]', 'X')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("XXX"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(3));
    }

    [Test]
    public async Task Test_StringGSub_EscapeSequences()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Test %% escape (literal %)
        var result = await state.DoStringAsync("return string.gsub('test', 'test', '100%%')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("100%"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(1));
    }

    [Test]
    public async Task Test_StringGSub_EdgeCases()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Empty string
        var result = await state.DoStringAsync("return string.gsub('', 'a', 'b')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo(""));
        Assert.That(result[1].Read<double>(), Is.EqualTo(0));

        // No matches
        result = await state.DoStringAsync("return string.gsub('hello', 'xyz', 'abc')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(0));

        // Zero replacement limit
        result = await state.DoStringAsync("return string.gsub('hello hello', 'hello', 'hi', 0)");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("hello hello"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(0));
    }

    [Test]
    public async Task Test_StringGSub_ComplexPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Email replacement
        var result = await state.DoStringAsync(@"
            local text = 'Contact john@example.com or jane@test.org'
            return string.gsub(text, '(%w+)@(%w+)%.(%w+)', function(user, domain, tld)
                return user:upper() .. '@' .. domain:upper() .. '.' .. tld:upper()
            end)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("Contact JOHN@EXAMPLE.COM or JANE@TEST.ORG"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // URL path extraction
        result = await state.DoStringAsync(@"
            return string.gsub('http://example.com/path/to/file.html', 
                               '^https?://[^/]+(/.*)', '%1')
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("/path/to/file.html"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(1));
    }

    [Test]
    public async Task Test_PatternMatching_Consistency()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Test that all string functions work consistently with same patterns
        var result = await state.DoStringAsync(@"
            local text = 'The quick brown fox jumps over the lazy dog'
            local pattern = '%a+'
            
            -- Test find
            local start, end_pos, word = string.find(text, '(' .. pattern .. ')')
            
            -- Test match  
            local match = string.match(text, pattern)
            
            -- Test gsub count
            local _, count = string.gsub(text, pattern, function(s) return s end)
            
            -- Test gmatch count
            local gmatch_count = 0
            for word in string.gmatch(text, pattern) do
                gmatch_count = gmatch_count + 1
            end
            
            return word, match, count, gmatch_count, start, end_pos
        ");

        Assert.That(result.Length, Is.EqualTo(6));
        Assert.That(result[0].Read<string>(), Is.EqualTo("The")); // find capture
        Assert.That(result[1].Read<string>(), Is.EqualTo("The")); // match result
        Assert.That(result[2].Read<double>(), Is.EqualTo(9)); // gsub count (9 words)
        Assert.That(result[3].Read<double>(), Is.EqualTo(9)); // gmatch count
        Assert.That(result[4].Read<double>(), Is.EqualTo(1)); // find start
        Assert.That(result[5].Read<double>(), Is.EqualTo(3)); // find end
    }

    [Test]
    public async Task Test_PatternMatching_SpecialPatterns()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Frontier pattern %f
        var result = await state.DoStringAsync(@"
            return string.gsub('hello world', '%f[%a]', '[')
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("[hello [world"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));

        // Minimal repetition with -
        result = await state.DoStringAsync("return string.match('aaab', 'a-b')");
        Assert.That(result[0].Read<string>(), Is.EqualTo("aaab"));

        // Optional quantifier ?
        result = await state.DoStringAsync("return string.gsub('color colour', 'colou?r', 'COLOR')");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("COLOR COLOR"));
        Assert.That(result[1].Read<double>(), Is.EqualTo(2));
    }

    [Test]
    public void Test_PatternMatching_ErrorCases()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();

        // Invalid pattern - missing closing bracket
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return string.match('test', '[abc')"));
        Assert.That(exception.Message, Does.Contain("missing ']'"));

        // Invalid pattern - missing %b arguments
        exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return string.match('test', '%b')"));
        Assert.That(exception.Message, Does.Contain("missing arguments to '%b'"));

        // Pattern too complex (exceeds recursion limit)
        exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return string.match(string.rep('a', 1000), string.rep('a?', 1000) .. string.rep('a', 1000))"));
        Assert.That(exception.Message, Does.Contain("pattern too complex"));
    }

    [Test]
    public async Task Test_DollarSignPattern_EscapingIssue()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test the problematic pattern from the user's code
        // The pattern "$([^$]+)" won't work because $ needs to be escaped as %$
        var result = await state.DoStringAsync(@"
            local prog = 'Hello $world$ and $123$ test'
            local matches = {}
            
            -- Wrong pattern (will not match correctly)
            for s in string.gmatch(prog, '$([^$]+)') do
                table.insert(matches, s)
            end
            
            return #matches
        ");
        Assert.That(result[0].Read<double>(), Is.EqualTo(4));

        // Test the correct pattern with escaped dollar signs
        result = await state.DoStringAsync(@"
            local prog = 'Hello $world$ and $123$ test'
            local matches = {}
            
            -- Correct pattern (with escaped dollar signs)
            for s in string.gmatch(prog, '%$([^%$]+)') do
                table.insert(matches, s)
            end
            
            return table.unpack(matches)
        ");
        Assert.That(result.Length, Is.EqualTo(4));
        Assert.That(result[0].Read<string>(), Is.EqualTo("world"));

        Assert.That(result[1].Read<string>(), Is.EqualTo(" and "));
        Assert.That(result[2].Read<string>(), Is.EqualTo("123"));
        Assert.That(result[3].Read<string>(), Is.EqualTo(" test"));
    }

    [Test]
    public async Task Test_DollarSignPattern_CompleteExample()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        state.OpenBasicLibrary();

        // Simulate the user's use case with corrected pattern
        var result = await state.DoStringAsync(@"
            local prog = 'Start $1$ middle $hello$ end $2$'
            local F = {
                [1] = function() return 'FIRST' end,
                [2] = function() return 'SECOND' end
            }
            local output = {}
            
            -- Process the string with correct pattern
            local lastPos = 1
            for match, content in string.gmatch(prog, '()%$([^%$]+)%$()') do
                -- Add text before the match
                if match > lastPos then
                    table.insert(output, prog:sub(lastPos, match - 1))
                end
                
                -- Process the content
                local n = tonumber(content)
                if n and F[n] then
                    table.insert(output, F[n]())
                else
                    table.insert(output, content)
                end
                
                lastPos = match + #content + 2 -- +2 for the two $ signs
            end
            
            -- Add remaining text
            if lastPos <= #prog then
                table.insert(output, prog:sub(lastPos))
            end
            
            return table.concat(output)
        ");

        Assert.That(result[0].Read<string>(), Is.EqualTo("Start FIRST middle hello end SECOND"));
    }

    [Test]
    public async Task Test_DollarSignPattern_EdgeCases()
    {
        var state = LuaState.Create();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        // Test empty content between dollar signs
        var result = await state.DoStringAsync(@"
            local matches = {}
            for s in string.gmatch('$$ and $empty$', '%$([^%$]*)') do
                table.insert(matches, s)
            end
            return table.unpack(matches)
        ");
        Assert.That(result.Length, Is.EqualTo(4));
        Assert.That(result[0].Read<string>(), Is.EqualTo("")); // Empty match
        Assert.That(result[1].Read<string>(), Is.EqualTo(" and ")); // Match with spaces
        Assert.That(result[2].Read<string>(), Is.EqualTo("empty"));
        Assert.That(result[3].Read<string>(), Is.EqualTo("")); // Trailing empty match

        // Test nested or adjacent dollar signs
        result = await state.DoStringAsync(@"
            local matches = {}
            for s in string.gmatch('$a$$b$', '%$([^%$]+)') do
                table.insert(matches, s)
            end
            return table.unpack(matches)
        ");
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Read<string>(), Is.EqualTo("a"));
        Assert.That(result[1].Read<string>(), Is.EqualTo("b"));
    }
}