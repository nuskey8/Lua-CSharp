namespace Lua.Standard.Internal;

sealed class MatchState(LuaState state, string source, string pattern)
{
    internal const int LuaMaxCaptures = 32;
    const int CapUnfinished = -1;
    const int CapPosition = -2;
    const char LEsc = '%';
    const string Specials = "^$*+?.([%-";
    internal const int MaxCalls = 200;

    internal struct Capture
    {
        public int Init;
        public int Len;

        public bool IsPosition => Len == CapPosition;
    }

    public readonly LuaState State = state;
    public readonly string Source = source;
    public readonly string Pattern = pattern;
    public int Level = 0;
    internal readonly Capture[] Captures = new Capture[LuaMaxCaptures];
    public int MatchDepth = MaxCalls;

    public static bool NoSpecials(ReadOnlySpan<char> pattern)
    {
#if NET8_0_OR_GREATER
        return !pattern.ContainsAny(Specials);
#else
        return pattern.IndexOfAny(Specials) == -1;
#endif
    }

    int StartCapture(int sIdx, int pIdx, int what)
    {
        if (Level >= LuaMaxCaptures)
        {
            throw new LuaRuntimeException(State, "too many captures");
        }

        Captures[Level].Init = sIdx;
        Captures[Level].Len = what;
        Level++;
        var res = Match(sIdx, pIdx);
        if (res < 0)
        {
            Level--;
        }

        return res;
    }

    int EndCapture(int sIdx, int pIdx)
    {
        var l = CaptureToClose();
        Captures[l].Len = sIdx - Captures[l].Init;
        var res = Match(sIdx, pIdx);
        if (res < 0)
        {
            Captures[l].Len = CapUnfinished; // Reset unfinished capture
        }

        return res;
    }

    public int Match(int sIdx, int pIdx)
    {
        if (MatchDepth-- == 0)
        {
            throw new LuaRuntimeException(State, "pattern too complex");
        }

        var endIdx = Pattern.Length;
    Init:
        if (pIdx < endIdx)
        {
            switch (Pattern[pIdx])
            {
                case '(':
                    if (pIdx + 1 < Pattern.Length && Pattern[pIdx + 1] == ')')
                    {
                        sIdx = StartCapture(sIdx, pIdx + 2, CapPosition);
                    }
                    else
                    {
                        sIdx = StartCapture(sIdx, pIdx + 1, CapUnfinished);
                    }

                    break;

                case ')':
                    // End capture

                    sIdx = EndCapture(sIdx, pIdx + 1);
                    break;


                case '$':
                    if (pIdx + 1 == Pattern.Length)
                    {
                        MatchDepth++;
                        return sIdx == Source.Length ? sIdx : -1;
                    }

                    goto Default;

                case LEsc:
                    if (pIdx + 1 >= Pattern.Length)
                    {
                        goto Default;
                    }

                    switch (Pattern[pIdx + 1])
                    {
                        case 'b':
                            {
                                sIdx = MatchBalance(sIdx, pIdx + 2);
                                if (sIdx < 0)
                                {
                                    MatchDepth++;
                                    return -1;
                                }

                                pIdx += 4;
                                goto Init;
                            }

                        case 'f':
                            if (pIdx + 2 < Pattern.Length && Pattern[pIdx + 2] == '[')
                            {
                                var ep = ClassEnd(Pattern, pIdx + 2);
                                var previous = sIdx > 0 ? Source[sIdx - 1] : '\0';
                                if (!MatchBracketClass(previous, Pattern, pIdx + 2, ep - 1) &&
                                    sIdx < Source.Length && MatchBracketClass(Source[sIdx], Pattern, pIdx + 2, ep - 1))
                                {
                                    pIdx = ep;
                                    goto Init;
                                }
                            }

                            sIdx = -1;

                            break;

                        case '0':
                        case '1':
                        case '2':
                        case '3':
                        case '4':
                        case '5':
                        case '6':
                        case '7':
                        case '8':
                        case '9':
                            {
                                sIdx = MatchCapture(sIdx, Pattern[pIdx + 1] - '1');
                                if (sIdx < 0)
                                {
                                    MatchDepth++;
                                    return -1;
                                }

                                pIdx += 2;
                                goto Init;
                            }

                        default:
                            goto Default;
                    }

                    break;

                default:
                Default:
                    {
                        var ep = ClassEnd(Pattern, pIdx);
                        if (!SingleMatch(sIdx, pIdx, ep))
                        {
                            if (ep < Pattern.Length && Pattern[ep] is '*' or '?' or '-')
                            {
                                pIdx = ep + 1;
                                goto Init; // Continue the while loop with updated pIdx
                            }
                            else
                            {
                                MatchDepth++;
                                return -1;
                            }
                        }
                        else
                        {
                            if (ep >= Pattern.Length)
                            {
                                // No quantifier, we matched one occurrence
                                sIdx++;
                                pIdx = ep; // Move past this pattern element
                                goto Init; // Continue matching with the rest of the pattern
                            }

                            switch (Pattern[ep])
                            {
                                case '?':
                                    {
                                        // Try matching with this character
                                        var res = Match(sIdx + 1, ep + 1);
                                        if (res >= 0)
                                        {
                                            MatchDepth++;
                                            return res;
                                        }

                                        pIdx = ep + 1;
                                        goto Init;
                                    }

                                case '+':
                                    // For +, we need at least one match (already verified)
                                    // Skip the first match we already verified
                                    sIdx++;
                                    // Now match zero or more additional occurrences
                                    goto case '*';

                                case '*':
                                    // Match zero or more occurrences
                                    {
                                        {
                                            var i = 0;
                                            // Count how many we can match
                                            while (sIdx + i < Source.Length && SingleMatch(sIdx + i, pIdx, ep))
                                            {
                                                i++;
                                            }

                                            // Try matching from longest to shortest
                                            while (i >= 0)
                                            {
                                                var res = Match(sIdx + i, ep + 1);
                                                if (res >= 0)
                                                {
                                                    MatchDepth++;
                                                    return res;
                                                }

                                                i--;
                                            }

                                            MatchDepth++;
                                            return -1;
                                        }
                                    }

                                case '-':
                                    // Match zero or more occurrences (minimal)
                                    {
                                        // for (;;) {
                                        //     const char *res = match(ms, s, ep+1);
                                        //     if (res != NULL)
                                        //         return res;
                                        //     else if (singlematch(ms, s, p, ep))
                                        //         s++;  /* try with one more repetition */
                                        //     else return NULL;
                                        // }
                                        while (true)
                                        {
                                            var res = Match(sIdx, ep + 1);
                                            if (res >= 0)
                                            {
                                                MatchDepth++;
                                                return res;
                                            }

                                            if (SingleMatch(sIdx, pIdx, ep))
                                            {
                                                sIdx++; // Try with one more repetition
                                            }
                                            else
                                            {
                                                MatchDepth++;
                                                return -1; // No match found
                                            }
                                        }
                                    }

                                default:
                                    sIdx++;
                                    pIdx = ep;
                                    goto Init; // Continue the while loop
                            }
                        }
                    }
            }
        }

        MatchDepth++;
        return sIdx;
    }

    bool SingleMatch(int sIdx, int pIdx, int ep)
    {
        if (sIdx >= Source.Length)
        {
            return false;
        }

        var c = Source[sIdx];
        switch (Pattern[pIdx])
        {
            case '.':
                return true;
            case LEsc:
                return pIdx + 1 < Pattern.Length && MatchClass(c, Pattern[pIdx + 1]);
            case '[':
                return MatchBracketClass(c, Pattern, pIdx, ep - 1);
            default:
                return Pattern[pIdx] == c;
        }
    }

    int CaptureToClose()
    {
        var level = Level;
        for (level--; level >= 0; level--)
        {
            if (Captures[level].Len == CapUnfinished)
            {
                return level;
            }
        }

        throw new LuaRuntimeException(State, "invalid pattern capture");
    }

    int MatchCapture(int sIdx, int l)
    {
        l = CheckCapture(l);
        var len = Captures[l].Len;
        if (len >= 0 && sIdx + len <= Source.Length)
        {
            var capture = Source.AsSpan(Captures[l].Init, len);
            if (sIdx + len <= Source.Length && Source.AsSpan(sIdx, len).SequenceEqual(capture))
            {
                return sIdx + len; // Return the  new position
            }
        }

        return -1;
    }

    int CheckCapture(int l)
    {
        if (l < 0 || l >= Level || Captures[l].Len == CapUnfinished)
        {
            throw new LuaRuntimeException(State, $"invalid capture index %{l + 1}");
        }

        return l;
    }

    int MatchBalance(int sIdx, int pIdx)
    {
        if (pIdx + 1 >= Pattern.Length)
        {
            throw new LuaRuntimeException(State, "malformed pattern (missing arguments to '%b')");
        }

        if (sIdx >= Source.Length || Source[sIdx] != Pattern[pIdx])
        {
            return -1;
        }

        var b = Pattern[pIdx];
        var e = Pattern[pIdx + 1];
        var cont = 1;
        sIdx++;

        while (sIdx < Source.Length)
        {
            if (Source[sIdx] == e)
            {
                if (--cont == 0)
                {
                    return sIdx + 1; // Return the length matched
                }
            }
            else if (Source[sIdx] == b)
            {
                cont++;
            }

            sIdx++;
        }

        return -1;
    }

    int ClassEnd(ReadOnlySpan<char> pattern, int pIdx)
    {
        switch (pattern[pIdx++])
        {
            case LEsc:
                if (pIdx >= pattern.Length)
                {
                    throw new LuaRuntimeException(State, "malformed pattern (ends with %)");
                }

                return pIdx + 1;

            case '[':
                if (pIdx < pattern.Length && pattern[pIdx] == '^')
                {
                    pIdx++;
                }

                do
                {
                    pIdx++;
                    if (pIdx < pattern.Length && pattern[pIdx] == LEsc)
                    {
                        pIdx++;
                    }

                    if (pIdx >= pattern.Length)
                    {
                        throw new LuaRuntimeException(State, "malformed pattern (missing ']')");
                    }
                } while (pIdx < pattern.Length && pattern[pIdx] != ']');

                return pIdx + 1;

            default:
                return pIdx;
        }
    }

    static bool MatchClass(char c, char cl)
    {
        bool res;
        switch (char.ToLower(cl))
        {
            case 'a': res = char.IsLetter(c); break;
            case 'c': res = char.IsControl(c); break;
            case 'd': res = char.IsDigit(c); break;
            case 'g': res = !char.IsControl(c) && !char.IsWhiteSpace(c); break;
            case 'l': res = char.IsLower(c); break;
            case 'p': res = char.IsPunctuation(c); break;
            case 's': res = char.IsWhiteSpace(c); break;
            case 'u': res = char.IsUpper(c); break;
            case 'w': res = char.IsLetterOrDigit(c); break;
            case 'x': res = IsHexDigit(c); break;
            case 'z': res = c == '\0'; break;
            default: return cl == c;
        }

        return char.IsLower(cl) ? res : !res;
    }

    static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    static bool MatchBracketClass(char c, ReadOnlySpan<char> pattern, int pIdx, int ec)
    {
        var sig = true;
        if (pIdx + 1 < pattern.Length && pattern[pIdx + 1] == '^')
        {
            sig = false;
            pIdx++;
        }

        while (++pIdx < ec)
        {
            if (pattern[pIdx] == LEsc)
            {
                pIdx++;
                if (pIdx <= ec && MatchClass(c, pattern[pIdx]))
                {
                    return sig;
                }
            }
            else if (pIdx + 2 < ec && pattern[pIdx + 1] == '-')
            {
                if (pattern[pIdx] <= c && c <= pattern[pIdx + 2])
                {
                    return sig;
                }

                pIdx += 2;
            }
            else if (pattern[pIdx] == c)
            {
                return sig;
            }
        }

        return !sig;
    }
}