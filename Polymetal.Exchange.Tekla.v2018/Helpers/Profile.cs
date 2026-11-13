using System;
using System.Collections.Generic;
using Tekla.Structures.Catalogs;

namespace Polymetal.Exchange.Tekla.v2018
{
    public class Profile
    {
        internal string ProfileName { get; }
        internal string ProfilePrefix { get; }
        internal bool IsParametric { get; }
        internal Dictionary<string, double> Parameters { get; }

        internal Profile(string profileName)
        {         
            IsParametric = false;            
            ProfileName = profileName;
            Parameters = new Dictionary<string, double>();

            var libraryProfileItem = new LibraryProfileItem();
            if (libraryProfileItem.Select(profileName)) return;            

            var profileItem = new ParametricProfileItem();
            if (profileItem.Select(profileName))
            {
                ProfilePrefix = profileItem.ProfilePrefix;
                ProfileName = $"{ProfilePrefix}_{profileItem.ParameterString}";
                var symbolList = profileItem.ParameterString
                    .Replace("-", " ").Replace("*", " ").Replace("[", " ").Replace("]", " ")
                    .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var symb in symbolList) Parameters.Add(symb, 0.0);

                foreach (ProfileItemParameter param in profileItem.aProfileItemParameters)
                {
                    if (!Parameters.ContainsKey(param.Symbol)) Parameters.Add(param.Symbol, 0.0);
                    Parameters[param.Symbol] = param.Value;
                }
                IsParametric = true;
            }            
        }
    }
}
