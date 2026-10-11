local all_data = {
	["The Merchant???"] = {
	Type = "Normal",
	BaseHP = "165",
	AscHP = "175",
	Image = "StS2_The Merchant???.png",
	Debut = "{{2|Hive}}",
	Encounters = {
		{
			location = "{{2|Hive}} and {{2|Glory}} Event Encounter",
			enemies = "[[File:The Merchant??? Icon.png|class=monster-icon|48px|link=StS2:The Merchant???]]"
		}
	},
	Intents = {
		{	Name = "Swipe",
			IntentIcons = { "Attack3" },
			Text = "Deals 13 ({{Asc2|9|15}}) damage.",
			AscText = {
				"Deals 13 damage.",
				"Deals {{Asc2|9|15}} damage."
			}
		},
		{	Name = "Spew Coins",
			IntentIcons = { "Attack3" },
			Text = "Deals 2 damage ×8.",
		},
		{	Name = "Throw Relic",
			IntentIcons = { "Attack3", "Debuff" },
			Text = "Deals 9 ({{Asc2|9|10}}) damage. Applies 1 {{BD2|Frail}}.",
			AscText = {
				"Deals 9 damage. Applies 1 {{BD2|Frail}}.",
				"Deals {{Asc2|9|10}} damage. Applies 1 {{BD2|Frail}}."
			}
		},
		{	Name = "Enrage",
			IntentIcons = { "Buff" },
			Text = "Gains 2 {{BD2|Strength}}."
		},
	}
},
["Battle Friend V1.0"] = {
	Type = "Normal",
	BaseHP = "75",
	Image = "StS2_BattleFriendV1.png",
	Debut = "{{2|Glory}}",
	StartsWith = "{{BD2|Time Limit}} 3",
	Encounters = {
		{
			location = "{{2|Glory}} Event Encounter",
			enemies = "[[File:Battle FriendV1 Icon.png|class=monster-icon|48px|link=StS2:Battle Friend]]"
		}
	},
	Intents = {
		{	Name = "Nothing",
			Text = "Does nothing.",
		},
	}
},
["Battle Friend V2.0"] = {
	Type = "Normal",
	BaseHP = "150",
	Image = "StS2_BattleFriendV2.png",
	Debut = "{{2|Glory}}",
	StartsWith = "{{BD2|Time Limit}} 3",
	Encounters = {
		{
			location = "{{2|Glory}} Event Encounter",
			enemies = "[[File:Battle FriendV2 Icon.png|class=monster-icon|48px|link=StS2:Battle Friend]]"
		}
	},
	Intents = {
		{	Name = "Nothing",
			Text = "Does nothing.",
		},
	}
},
["Battle Friend V3.0"] = {
	Type = "Normal",
	BaseHP = "300",
	Image = "StS2_BattleFriendV3.png",
	Debut = "{{2|Glory}}",
	StartsWith = "{{BD2|Time Limit}} 3",
	Encounters = {
		{
			location = "{{2|Glory}} Event Encounter",
			enemies = "[[File:Battle Friend IconV3.png|class=monster-icon|48px|link=StS2:Battle Friend]]"
		}
	},
	Intents = {
		{	Name = "Nothing",
			Text = "Does nothing.",
		},
	}
},
}

local formatted = {}
for name, enemy in pairs(all_data) do
	enemy.EditLink = "Module:Enemies/StS2_data/Events"
	formatted[name] = enemy
end

return formatted