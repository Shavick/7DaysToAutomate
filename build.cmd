SET DST="%APPDATA%\7DaysToDie\Mods\7DaysToAutomate"
robocopy . %DST% *.md *.dll *.pdb /dcopy:dat /PURGE
robocopy .\Config %DST%\Config *.xml /s /dcopy:dat /PURGE
robocopy .\Config %DST%\Config *.txt /s /dcopy:dat /PURGE