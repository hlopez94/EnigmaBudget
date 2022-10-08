SET @script_version = '0.1.0';
SELECT @ejecutado := enigma.fn_leveling_script_ejecutado(@script_version);
select @errmsg := CONCAT('SCRIPT ', @script_version, ' EJECUTADO EN FECHA ', @ejecutado);
DELIMITER $$
$$
IF @ejecutado
THEN 
	SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT = @errmsg;
END IF;
$$
DELIMITER ;

CREATE TABLE enigma.usuarios (
	usu_id BINARY(16) NOT NULL COMMENT 'ID Unico para el usuario',
	usu_usuario VARCHAR(100) NOT NULL COMMENT 'Nombre de usuario',
	usu_correo VARCHAR(100) NOT NULL COMMENT 'Correo asociado al Usuario',
	usu_password VARCHAR(256) NULL COMMENT 'Clave hasheada del usuario',
	usu_seed VARCHAR(64) NULL COMMENT 'Semilla de hasheo de la clave de usuario',
    usu_fecha_alta DATETIME NOT NULL COMMENT  'Fecha de alta del usuario',
    usu_fecha_modif DATETIME NOT NULL COMMENT  'Fecha de modificación del usuario',
    usu_fecha_baja DATETIME NULL COMMENT  'Fecha de baja del usuario',
	usu_correo_validado BOOL DEFAULT 0 NOT NULL COMMENT 'Indica si el mail brindado por el usuario fue validado',
	CONSTRAINT PK_usuarios PRIMARY KEY (usu_id)
)
COMMENT='Tabla con datos de usuarios';
commit;
ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_correo UNIQUE (usu_correo);
ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_usuario UNIQUE (usu_usuario);

CREATE INDEX IDX_usuarios_usu_correo ON enigma.usuarios (usu_correo);
CREATE INDEX IDX_usuarios_usu_usuario ON enigma.usuarios (usu_usuario);


DELIMITER $$
$$
CREATE TRIGGER TG_USUARIOS_INS
BEFORE INSERT 
	ON usuarios FOR EACH ROW BEGIN 
    	SET new.usu_id = (UNHEX(REPLACE(UUID(),'-','')));  
   		SET new.usu_fecha_alta = SYSDATE(); 
		SET new.usu_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;
	
DELIMITER $$
$$
CREATE TRIGGER TG_USUARIOS_UPD
BEFORE UPDATE 
	ON usuarios FOR EACH ROW BEGIN 
		IF old.usu_id <> new.usu_id THEN 
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='No se puede cambiar ID';
		END IF;		
		SET new.usu_fecha_modif = SYSDATE(); 
	END;$$
DELIMITER ;

-- enigma.usuario_perfil definition
CREATE TABLE enigma.usuario_perfil (
	usp_usu_id binary(16) NOT NULL,
	usp_nombre varchar(100) NULL,
	usp_fecha_nacimiento DATE NULL,
	usp_tel_cod_pais SMALLINT NULL,
	usp_tel_cod_area SMALLINT NULL,
	usp_tel_nro INT NULL,
	usp_fecha_alta datetime NOT NULL,
	usp_fecha_modif datetime NOT NULL,
	usp_fecha_baja datetime NULL,
	CONSTRAINT PK_usuario_perfil PRIMARY KEY (usp_usu_id),
	CONSTRAINT FK_usuario_perfil_usuarios FOREIGN KEY (usp_usu_id) REFERENCES enigma.usuarios(usu_id),
	CONSTRAINT CHK_usuario_perfil_tel CHECK (
					(
						usp_tel_cod_pais IS NOT NULL AND 
                    	usp_tel_cod_area IS NOT NULL AND
                    	usp_tel_nro IS NOT NULL 
                    ) 
                    OR
                    (
                    	usp_tel_cod_pais IS NULL AND 
                    	usp_tel_cod_area IS NULL AND
                    	usp_tel_nro IS NULL 
                    )
                    )
);

DELIMITER $$
$$
CREATE TRIGGER TG_USUARIO_PERFIL_INS
BEFORE INSERT
ON usuario_perfil FOR EACH ROW
BEGIN 
	SET NEW.usp_fecha_alta = NOW();
	SET NEW.usp_fecha_modif = NOW();
END;$$
DELIMITER ;

DELIMITER $$
$$CREATE TRIGGER TG_USUARIO_PERFIL_UPD
BEFORE UPDATE
ON usuario_perfil FOR EACH ROW
BEGIN 
	IF old.usp_usu_id <> new.usp_usu_id THEN 
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT='No se puede cambiar ID';
		END IF;

	SET NEW.usp_fecha_modif = NOW();
END;
$$
DELIMITER ;

CREATE TABLE enigma.paises (
	pai_iso2 varchar(2) NOT NULL,
	pai_iso3 varchar(3) NOT NULL,
	pai_nombre varchar(100) NOT NULL,
	pai_nombre_int varchar(100) NOT NULL,
	pai_phone_code int NOT NULL,
	CONSTRAINT paises_PK PRIMARY KEY (pai_iso2)
);

INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('AD','AND','Andorra','Andorra',376),
	 ('AE','ARE','Emiratos Árabes Unidos','United Arab Emirates',971),
	 ('AF','AFG','Afganistán','Afghanistan',93),
	 ('AG','ATG','Antigua y Barbuda','Antigua and Barbuda',1268),
	 ('AI','AIA','Anguila','Anguilla',1264),
	 ('AL','ALB','Albania','Albania',355),
	 ('AM','ARM','Armenia','Armenia',374),
	 ('AO','AGO','Angola','Angola',244),
	 ('AQ','ATA','Antártida','Antarctica',672),
	 ('AR','ARG','Argentina','Argentina',54);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('AS','ASM','Samoa Americana','American Samoa',1684),
	 ('AT','AUT','Austria','Austria',43),
	 ('AU','AUS','Australia','Australia',61),
	 ('AW','ABW','Aruba','Aruba',297),
	 ('AX','ALA','Islas de Åland','Åland Islands',358),
	 ('AZ','AZE','Azerbaiyán','Azerbaijan',994),
	 ('BA','BIH','Bosnia y Herzegovina','Bosnia and Herzegovina',387),
	 ('BB','BRB','Barbados','Barbados',1246),
	 ('BD','BGD','Bangladesh','Bangladesh',880),
	 ('BE','BEL','Bélgica','Belgium',32);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('BF','BFA','Burkina Faso','Burkina Faso',226),
	 ('BG','BGR','Bulgaria','Bulgaria',359),
	 ('BH','BHR','Bahrein','Bahrain',973),
	 ('BI','BDI','Burundi','Burundi',257),
	 ('BJ','BEN','Benín','Benin',229),
	 ('BL','BLM','San Bartolomé','Saint Barthélemy',590),
	 ('BM','BMU','Islas Bermudas','Bermuda Islands',1441),
	 ('BN','BRN','Brunéi','Brunei',673),
	 ('BO','BOL','Bolivia','Bolivia',591),
	 ('BR','BRA','Brasil','Brazil',55);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('BS','BHS','Bahamas','Bahamas',1242),
	 ('BT','BTN','Bhután','Bhutan',975),
	 ('BV','BVT','Isla Bouvet','Bouvet Island',0),
	 ('BW','BWA','Botsuana','Botswana',267),
	 ('BY','BLR','Bielorrusia','Belarus',375),
	 ('BZ','BLZ','Belice','Belize',501),
	 ('CA','CAN','Canadá','Canada',1),
	 ('CC','CCK','Islas Cocos (Keeling)','Cocos (Keeling) Islands',61),
	 ('CD','COD','República Democrática del Congo','Democratic Republic of the Congo',243),
	 ('CF','CAF','República Centroafricana','Central African Republic',236);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('CG','COG','República del Congo','Republic of the Congo',242),
	 ('CH','CHE','Suiza','Switzerland',41),
	 ('CI','CIV','Costa de Marfil','Ivory Coast',225),
	 ('CK','COK','Islas Cook','Cook Islands',682),
	 ('CL','CHL','Chile','Chile',56),
	 ('CM','CMR','Camerún','Cameroon',237),
	 ('CN','CHN','China','China',86),
	 ('CO','COL','Colombia','Colombia',57),
	 ('CR','CRI','Costa Rica','Costa Rica',506),
	 ('CU','CUB','Cuba','Cuba',53);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('CV','CPV','Cabo Verde','Cape Verde',238),
	 ('CW','CWU','Curazao','Curaçao',5999),
	 ('CX','CXR','Isla de Navidad','Christmas Island',61),
	 ('CY','CYP','Chipre','Cyprus',357),
	 ('CZ','CZE','República Checa','Czech Republic',420),
	 ('DE','DEU','Alemania','Germany',49),
	 ('DJ','DJI','Yibuti','Djibouti',253),
	 ('DK','DNK','Dinamarca','Denmark',45),
	 ('DM','DMA','Dominica','Dominica',1767),
	 ('DO','DOM','República Dominicana','Dominican Republic',1809);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('DZ','DZA','Argelia','Algeria',213),
	 ('EC','ECU','Ecuador','Ecuador',593),
	 ('EE','EST','Estonia','Estonia',372),
	 ('EG','EGY','Egipto','Egypt',20),
	 ('EH','ESH','Sahara Occidental','Western Sahara',212),
	 ('ER','ERI','Eritrea','Eritrea',291),
	 ('ES','ESP','España','Spain',34),
	 ('ET','ETH','Etiopía','Ethiopia',251),
	 ('FI','FIN','Finlandia','Finland',358),
	 ('FJ','FJI','Fiyi','Fiji',679);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('FK','FLK','Islas Malvinas','Falkland Islands (Malvinas)',500),
	 ('FM','FSM','Micronesia','Estados Federados de',691),
	 ('FO','FRO','Islas Feroe','Faroe Islands',298),
	 ('FR','FRA','Francia','France',33),
	 ('GA','GAB','Gabón','Gabon',241),
	 ('GB','GBR','Reino Unido','United Kingdom',44),
	 ('GD','GRD','Granada','Grenada',1473),
	 ('GE','GEO','Georgia','Georgia',995),
	 ('GF','GUF','Guayana Francesa','French Guiana',594),
	 ('GG','GGY','Guernsey','Guernsey',44);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('GH','GHA','Ghana','Ghana',233),
	 ('GI','GIB','Gibraltar','Gibraltar',350),
	 ('GL','GRL','Groenlandia','Greenland',299),
	 ('GM','GMB','Gambia','Gambia',220),
	 ('GN','GIN','Guinea','Guinea',224),
	 ('GP','GLP','Guadalupe','Guadeloupe',590),
	 ('GQ','GNQ','Guinea Ecuatorial','Equatorial Guinea',240),
	 ('GR','GRC','Grecia','Greece',30),
	 ('GS','SGS','Islas Georgias del Sur y Sandwich del Sur','South Georgia and the South Sandwich Islands',500),
	 ('GT','GTM','Guatemala','Guatemala',502);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('GU','GUM','Guam','Guam',1671),
	 ('GW','GNB','Guinea-Bissau','Guinea-Bissau',245),
	 ('GY','GUY','Guyana','Guyana',592),
	 ('HK','HKG','Hong kong','Hong Kong',852),
	 ('HM','HMD','Islas Heard y McDonald','Heard Island and McDonald Islands',0),
	 ('HN','HND','Honduras','Honduras',504),
	 ('HR','HRV','Croacia','Croatia',385),
	 ('HT','HTI','Haití','Haiti',509),
	 ('HU','HUN','Hungría','Hungary',36),
	 ('ID','IDN','Indonesia','Indonesia',62);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('IE','IRL','Irlanda','Ireland',353),
	 ('IL','ISR','Israel','Israel',972),
	 ('IM','IMN','Isla de Man','Isle of Man',44),
	 ('IN','IND','India','India',91),
	 ('IO','IOT','Territorio Británico del Océano Índico','British Indian Ocean Territory',246),
	 ('IQ','IRQ','Irak','Iraq',964),
	 ('IR','IRN','Irán','Iran',98),
	 ('IS','ISL','Islandia','Iceland',354),
	 ('IT','ITA','Italia','Italy',39),
	 ('JE','JEY','Jersey','Jersey',44);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('JM','JAM','Jamaica','Jamaica',1876),
	 ('JO','JOR','Jordania','Jordan',962),
	 ('JP','JPN','Japón','Japan',81),
	 ('KE','KEN','Kenia','Kenya',254),
	 ('KG','KGZ','Kirguistán','Kyrgyzstan',996),
	 ('KH','KHM','Camboya','Cambodia',855),
	 ('KI','KIR','Kiribati','Kiribati',686),
	 ('KM','COM','Comoras','Comoros',269),
	 ('KN','KNA','San Cristóbal y Nieves','Saint Kitts and Nevis',1869),
	 ('KP','PRK','Corea del Norte','North Korea',850);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('KR','KOR','Corea del Sur','South Korea',82),
	 ('KW','KWT','Kuwait','Kuwait',965),
	 ('KY','CYM','Islas Caimán','Cayman Islands',1345),
	 ('KZ','KAZ','Kazajistán','Kazakhstan',7),
	 ('LA','LAO','Laos','Laos',856),
	 ('LB','LBN','Líbano','Lebanon',961),
	 ('LC','LCA','Santa Lucía','Saint Lucia',1758),
	 ('LI','LIE','Liechtenstein','Liechtenstein',423),
	 ('LK','LKA','Sri lanka','Sri Lanka',94),
	 ('LR','LBR','Liberia','Liberia',231);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('LS','LSO','Lesoto','Lesotho',266),
	 ('LT','LTU','Lituania','Lithuania',370),
	 ('LU','LUX','Luxemburgo','Luxembourg',352),
	 ('LV','LVA','Letonia','Latvia',371),
	 ('LY','LBY','Libia','Libya',218),
	 ('MA','MAR','Marruecos','Morocco',212),
	 ('MC','MCO','Mónaco','Monaco',377),
	 ('MD','MDA','Moldavia','Moldova',373),
	 ('ME','MNE','Montenegro','Montenegro',382),
	 ('MF','MAF','San Martín (Francia)','Saint Martin (French part)',1599);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('MG','MDG','Madagascar','Madagascar',261),
	 ('MH','MHL','Islas Marshall','Marshall Islands',692),
	 ('MK','MKD','Macedônia','Macedonia',389),
	 ('ML','MLI','Mali','Mali',223),
	 ('MM','MMR','Birmania','Myanmar',95),
	 ('MN','MNG','Mongolia','Mongolia',976),
	 ('MO','MAC','Macao','Macao',853),
	 ('MP','MNP','Islas Marianas del Norte','Northern Mariana Islands',1670),
	 ('MQ','MTQ','Martinica','Martinique',596),
	 ('MR','MRT','Mauritania','Mauritania',222);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('MS','MSR','Montserrat','Montserrat',1664),
	 ('MT','MLT','Malta','Malta',356),
	 ('MU','MUS','Mauricio','Mauritius',230),
	 ('MV','MDV','Islas Maldivas','Maldives',960),
	 ('MW','MWI','Malawi','Malawi',265),
	 ('MX','MEX','México','Mexico',52),
	 ('MY','MYS','Malasia','Malaysia',60),
	 ('MZ','MOZ','Mozambique','Mozambique',258),
	 ('NA','NAM','Namibia','Namibia',264),
	 ('NC','NCL','Nueva Caledonia','New Caledonia',687);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('NE','NER','Niger','Niger',227),
	 ('NF','NFK','Isla Norfolk','Norfolk Island',672),
	 ('NG','NGA','Nigeria','Nigeria',234),
	 ('NI','NIC','Nicaragua','Nicaragua',505),
	 ('NL','NLD','Países Bajos','Netherlands',31),
	 ('NO','NOR','Noruega','Norway',47),
	 ('NP','NPL','Nepal','Nepal',977),
	 ('NR','NRU','Nauru','Nauru',674),
	 ('NU','NIU','Niue','Niue',683),
	 ('NZ','NZL','Nueva Zelanda','New Zealand',64);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('OM','OMN','Omán','Oman',968),
	 ('PA','PAN','Panamá','Panama',507),
	 ('PE','PER','Perú','Peru',51),
	 ('PF','PYF','Polinesia Francesa','French Polynesia',689),
	 ('PG','PNG','Papúa Nueva Guinea','Papua New Guinea',675),
	 ('PH','PHL','Filipinas','Philippines',63),
	 ('PK','PAK','Pakistán','Pakistan',92),
	 ('PL','POL','Polonia','Poland',48),
	 ('PM','SPM','San Pedro y Miquelón','Saint Pierre and Miquelon',508),
	 ('PN','PCN','Islas Pitcairn','Pitcairn Islands',870);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('PR','PRI','Puerto Rico','Puerto Rico',1),
	 ('PS','PSE','Palestina','Palestine',970),
	 ('PT','PRT','Portugal','Portugal',351),
	 ('PW','PLW','Palau','Palau',680),
	 ('PY','PRY','Paraguay','Paraguay',595),
	 ('QA','QAT','Qatar','Qatar',974),
	 ('RE','REU','Reunión','Réunion',262),
	 ('RO','ROU','Rumanía','Romania',40),
	 ('RS','SRB','Serbia','Serbia',381),
	 ('RU','RUS','Rusia','Russia',7);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('RW','RWA','Ruanda','Rwanda',250),
	 ('SA','SAU','Arabia Saudita','Saudi Arabia',966),
	 ('SB','SLB','Islas Salomón','Solomon Islands',677),
	 ('SC','SYC','Seychelles','Seychelles',248),
	 ('SD','SDN','Sudán','Sudan',249),
	 ('SE','SWE','Suecia','Sweden',46),
	 ('SG','SGP','Singapur','Singapore',65),
	 ('SH','SHN','Santa Elena','Ascensión y Tristán de Acuña',290),
	 ('SI','SVN','Eslovenia','Slovenia',386),
	 ('SJ','SJM','Svalbard y Jan Mayen','Svalbard and Jan Mayen',47);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('SK','SVK','Eslovaquia','Slovakia',421),
	 ('SL','SLE','Sierra Leona','Sierra Leone',232),
	 ('SM','SMR','San Marino','San Marino',378),
	 ('SN','SEN','Senegal','Senegal',221),
	 ('SO','SOM','Somalia','Somalia',252),
	 ('SR','SUR','Surinám','Suriname',597),
	 ('SS','SSD','República de Sudán del Sur','South Sudan',211),
	 ('ST','STP','Santo Tomé y Príncipe','Sao Tome and Principe',239),
	 ('SV','SLV','El Salvador','El Salvador',503),
	 ('SX','SMX','Sint Maarten','Sint Maarten',0);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('SY','SYR','Siria','Syria',963),
	 ('SZ','SWZ','Swazilandia','Swaziland',268),
	 ('TC','TCA','Islas Turcas y Caicos','Turks and Caicos Islands',1649),
	 ('TD','TCD','Chad','Chad',235),
	 ('TF','ATF','Territorios Australes y Antárticas Franceses','French Southern Territories',0),
	 ('TG','TGO','Togo','Togo',228),
	 ('TH','THA','Tailandia','Thailand',66),
	 ('TJ','TJK','Tayikistán','Tajikistan',992),
	 ('TK','TKL','Tokelau','Tokelau',690),
	 ('TL','TLS','Timor Oriental','East Timor',670);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('TM','TKM','Turkmenistán','Turkmenistan',993),
	 ('TN','TUN','Tunez','Tunisia',216),
	 ('TO','TON','Tonga','Tonga',676),
	 ('TR','TUR','Turquía','Turkey',90),
	 ('TT','TTO','Trinidad y Tobago','Trinidad and Tobago',1868),
	 ('TV','TUV','Tuvalu','Tuvalu',688),
	 ('TW','TWN','Taiwán','Taiwan',886),
	 ('TZ','TZA','Tanzania','Tanzania',255),
	 ('UA','UKR','Ucrania','Ukraine',380),
	 ('UG','UGA','Uganda','Uganda',256);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('UM','UMI','Islas Ultramarinas Menores de Estados Unidos','United States Minor Outlying Islands',246),
	 ('US','USA','Estados Unidos de América','United States of America',1),
	 ('UY','URY','Uruguay','Uruguay',598),
	 ('UZ','UZB','Uzbekistán','Uzbekistan',998),
	 ('VA','VAT','Ciudad del Vaticano','Vatican City State',39),
	 ('VC','VCT','San Vicente y las Granadinas','Saint Vincent and the Grenadines',1784),
	 ('VE','VEN','Venezuela','Venezuela',58),
	 ('VG','VGB','Islas Vírgenes Británicas','Virgin Islands',1284),
	 ('VI','VIR','Islas Vírgenes de los Estados Unidos','United States Virgin Islands',1340),
	 ('VN','VNM','Vietnam','Vietnam',84);
INSERT INTO enigma.paises (pai_iso2,pai_iso3,pai_nombre,pai_nombre_int,pai_phone_code) VALUES
	 ('VU','VUT','Vanuatu','Vanuatu',678),
	 ('WF','WLF','Wallis y Futuna','Wallis and Futuna',681),
	 ('WS','WSM','Samoa','Samoa',685),
	 ('YE','YEM','Yemen','Yemen',967),
	 ('YT','MYT','Mayotte','Mayotte',262),
	 ('ZA','ZAF','Sudáfrica','South Africa',27),
	 ('ZM','ZMB','Zambia','Zambia',260),
	 ('ZW','ZWE','Zimbabue','Zimbabwe',263);

CREATE TABLE enigma.usuarios_validacion_email (
	uve_usu_id binary(16) NOT NULL,
	uve_id INT auto_increment NOT NULL,
	uve_fecha_alta datetime NOT NULL,
	uve_fecha_baja datetime NOT NULL,
	uve_salt varchar(64) NOT NULL,
	uve_validado bool DEFAULT 0 NOT NULL,
	CONSTRAINT usuarios_validacion_email_PK PRIMARY KEY (uve_id),
	CONSTRAINT usuarios_validacion_email_FK FOREIGN KEY (uve_usu_id) REFERENCES enigma.usuarios(usu_id)
)

CREATE INDEX usuarios_validacion_email_uve_ID_IDX USING BTREE ON enigma.usuarios_validacion_email (uve_id);
CREATE INDEX usuarios_validacion_email_uve_salt_IDX USING BTREE ON enigma.usuarios_validacion_email (uve_salt,uve_id);

DELIMITER $$
$$ CREATE TRIGGER trg_uve_upd
AFTER UPDATE
ON usuarios_validacion_email FOR EACH ROW
BEGIN
 	IF (new.uve_validado)
		UPDATE enigma.usuarios
			SET usu.usu_correo = new.uve_nuevo_correo,
			SET usu.usu_correo_validado = TRUE 
		WHERE usu.usu_id = new.uve_usu_id;
	END IF;
	SET NEW.uve_fecha_baja = NOW();
END;
$$
DELIMITER ;

CALL enigma.sp_leveling_script_ejecutado(@script_version);
SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ' , @script_version ) estado_ejecucion;

