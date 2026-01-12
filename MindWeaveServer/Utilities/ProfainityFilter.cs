using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MindWeaveServer.Utilities
{
    public static class ProfanityFilter
    {
        private static readonly string[] rawDeniedWords = new string[]
        {
            "stupid","idiot","dumb","badword","hell", "ass","damn", 
            "fuck","fucking","fuckoff","shit","bullshit","crap","bastard","moron","jerk",
            "asshole","idiotic","dumbass","dipshit","sonofabitch","bitch","bitches",
            "bloody","piss","pissed","retard","retarded","loser","scumbag","trash",

            "groseria","estupido","idiota","basura","inutil","maldito",
            "pendejo","pendeja","cabron","cabrona","chingar","chingada","chingado",
            "chingadera","mierda","pinche","cagon","cagona","putazo","madrazo","carajo",
            "coño","jodido","jodida","joder","jodete","zorra","zorro","baboso","babosa",
            "tarado","tarada","tonto","tonta","bobo","boba","asqueroso","asquerosa",
            "imbecil","imbécil","malparido","malnacido","culero","culera","güey","wey",
            "perra","perro", "putamadre", "puto", "puta", "putos", "putas", "verga", "pito", "retrasado", "retrasada"
        };

        private static readonly Dictionary<char, char> leetMap = new Dictionary<char, char>
        {
            {'4','a'}, {'@','a'}, {'3','e'}, {'1','i'}, {'!','i'}, {'0','o'}, {'5','s'}, {'$','s'}, {'7','t'}, {'+','t'}
        };

        private static readonly Regex profanityRegex = CreateProfanityRegex();

        private static Regex CreateProfanityRegex()
        {
            var deniedWordsSet1 = new HashSet<string>();
            foreach (var word in rawDeniedWords)
            {
                deniedWordsSet1.Add(normalize(word));
            }
            var escapedWords = deniedWordsSet1.Select(Regex.Escape);
            string pattern = @"\b(" + string.Join("|", escapedWords) + @")[a-z]*\b";
            return new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)
            );
        }

        public static bool containsProfanity(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string normalized = normalize(message);
            normalized = normalizeSymbols(normalized);
            string leetCleaned = normalizeLeet(normalized);

            return profanityRegex.IsMatch(leetCleaned);
        }

        private static string normalize(string input)
        {
            string formD = input.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (char ch in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string normalizeLeet(string word)
        {
            var result = new StringBuilder(word.Length);
            foreach (char c in word)
            {
                if (leetMap.TryGetValue(c, out char replacement))
                {
                    result.Append(replacement);
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        private static string normalizeSymbols(string word)
        {
            char[] removableSymbols = { '.', '_', '-', '*', '~', ',', ';', ':', '|', '/', '\\', ' ' };

            return string.Concat(word.Where(c => !removableSymbols.Contains(c)));
        }
    }
}