/* ****************************************************************************
 *
 * Copyright (c) Jeff Hardy.
 * 
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Apache License, Version 2.0, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

#if !FEATURE_SECURITY_RULES

namespace System.Security {
    public enum SecurityRuleSet {
        None, Level1, Level2
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class SecurityRulesAttribute : Attribute {
        readonly SecurityRuleSet ruleSet;

        // This is a positional argument
        public SecurityRulesAttribute(SecurityRuleSet ruleSet) {
            this.ruleSet = ruleSet;
        }

        public SecurityRuleSet RuleSet {
            get { return ruleSet; }
        }
        
        public bool SkipVerificationInFullTrust { get; set; }
    }
}

#endif