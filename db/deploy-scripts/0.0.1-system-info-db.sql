DELIMITER |
BEGIN NOT ATOMIC
	CREATE TABLE IF NOT EXISTS sql_leveling_info(
		sli_script_version VARCHAR(16) NOT NULL COMMENT 'Version de script ejecutado',
		sli_script_fecha_ejecucion DATETIME NOT NULL COMMENT 'Fecha de ejecución del script',
		sli_script_resultado TEXT NULL COMMENT 'Información de la ejecución',
		CONSTRAINT  PRIMARY KEY (sli_script_version)
	) COMMENT='Tabla con datos de usuarios';

	IF (NOT EXISTS (SELECT * FROM sql_leveling_info WHERE sli_script_version = '0.0.1'))
		THEN 
			INSERT INTO enigma.sql_leveling_info (sli_script_version, sli_script_fecha_ejecucion, sli_script_resultado) VALUES('0.0.1', SYSDATE(), 'OK');
	END IF;
END|
DELIMITER;