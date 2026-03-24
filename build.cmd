SET DST="%APPDATA%\7DaysToDie\Mods\7DaysToAutomate"
SET DEDICATED_DST="C:\Program Files (x86)\Steam\steamapps\common\7 Days to Die Dedicated Server\Mods\7DaysToAutomate"
robocopy . %DST% *.md *.dll *.pdb /dcopy:dat /PURGE
robocopy .\Config %DST%\Config *.xml /s /dcopy:dat /PURGE
robocopy .\Config %DST%\Config *.txt /s /dcopy:dat /PURGE

robocopy . %DEDICATED_DST% *.md *.dll *.pdb /dcopy:dat /PURGE
robocopy .\Config %DEDICATED_DST%\Config *.xml /s /dcopy:dat /PURGE
robocopy .\Config %DEDICATED_DST%\Config *.txt /s /dcopy:dat /PURGE
