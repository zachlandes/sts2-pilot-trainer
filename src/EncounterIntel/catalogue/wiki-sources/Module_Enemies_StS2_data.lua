-- IMPORTANT! This module is split into separate submodules, if you want to make an edit to a particular keyword, find their submodule first.

local ALL_DATA = {
	require('Module:Enemies/StS2 data/Overgrowth'), --contains regular enemies in Overgrowth
	require('Module:Enemies/StS2 data/Underdocks'), --contains regular enemies in Underdocks
	require('Module:Enemies/StS2 data/Hive'), --contains regular enemies in Hive
	require('Module:Enemies/StS2 data/Glory'), --contains regular enemies in Glory
	require('Module:Enemies/StS2 data/Elites'), --contains Elite enemies
	require('Module:Enemies/StS2 data/Bosses'), --contains Boss enemies
	require('Module:Enemies/StS2 data/Events'), --contains Event only enemies
}


local data = {}

for _, enemies in ipairs(ALL_DATA) do
	for enemy_name, enemy in pairs(enemies) do
		data[enemy_name] = enemy
	end
end

return data