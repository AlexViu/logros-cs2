-- Logros de Navidad 2026 (ca_achievements ya creada; requiere la versión con ActiveFrom/ActiveTo, que añade las columnas al cargar).
-- Hora local del servidor (UtcOffsetHours). Fuera de su ventana no se pueden conseguir y solo los ve quien ya los tiene.
-- Los de tipo Event los da el plugin AdventCalendar (AchievementHooks -> css_logro_give). Tras ejecutarlo: css_logros_reload
SET NAMES utf8mb4;
INSERT INTO ca_achievements (Id,SortOrder,Enabled,Name,Description,Type,Target,Map,Style,Points,Hidden,Command,RewardCredits,RewardItems,ActiveFrom,ActiveTo) VALUES
 ('xmas_advent_1',   1010,1,'Primera casilla',      'Abre tu primera casilla del calendario de adviento',                 'Event',          1,     '',NULL, 10,0,'', 200,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_advent_10',  1020,1,'Espíritu navideño',    'Abre 10 casillas del calendario de adviento',                        'Event',          1,     '',NULL, 30,0,'',1000,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_advent_25',  1030,1,'Calendario completo',  'Abre las 25 casillas del calendario de adviento',                    'Event',          1,     '',NULL,100,0,'',5000,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_nochebuena', 1040,1,'Nochebuena',           'Juega en el servidor el 24 de diciembre',                            'Event',          1,     '',NULL, 15,0,'', 500,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_navidad',    1050,1,'¡Feliz Navidad!',      'Juega en el servidor el 25 de diciembre',                            'Event',          1,     '',NULL, 15,0,'', 500,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_course',     1060,1,'Recorrido navideño',   'Completa un recorrido durante las fiestas',                          'FinishUnder',    999999,'',NULL, 10,0,'', 300,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_marathon',   1070,1,'Atracón de turrón',    'Completa 10 recorridos sin salir del servidor durante las fiestas',  'SessionFinishes',10,    '',NULL, 25,0,'',1000,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_top3',       1080,1,'Regalo de podio',      'Termina un mapa entre los 3 mejores tiempos durante las fiestas',    'FinishTop',      3,     '',NULL, 40,0,'',1500,'','2026-12-01 00:00:00','2027-01-06 23:59:59'),
 ('xmas_nochevieja', 1090,1,'Último salto del año', 'Completa un recorrido en Nochevieja',                                'FinishUnder',    999999,'',NULL, 30,1,'',1000,'','2026-12-31 00:00:00','2026-12-31 23:59:59'),
 ('xmas_reyes',      1100,1,'Regalo de Reyes',      'Completa un recorrido el día de Reyes',                              'FinishUnder',    999999,'',NULL, 30,1,'',1000,'','2027-01-06 00:00:00','2027-01-06 23:59:59')
ON DUPLICATE KEY UPDATE Name=VALUES(Name), Description=VALUES(Description), Type=VALUES(Type), Target=VALUES(Target), Points=VALUES(Points),
 Hidden=VALUES(Hidden), RewardCredits=VALUES(RewardCredits), RewardItems=VALUES(RewardItems), ActiveFrom=VALUES(ActiveFrom), ActiveTo=VALUES(ActiveTo);
