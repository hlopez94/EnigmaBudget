CREATE TABLE IF NOT EXISTS enigma.sql_leveling_info(
		sli_script_version VARCHAR(16) NOT NULL COMMENT 'Version de script ejecutado',
		sli_script_fecha_ejecucion DATETIME NOT NULL COMMENT 'Fecha de ejecución del script',
		sli_script_resultado TEXT NULL COMMENT 'Información de la ejecución',
		CONSTRAINT  PRIMARY KEY (sli_script_version)
	) COMMENT='Tabla con datos de usuarios';

DELIMITER $$
$$
CREATE FUNCTION IF NOT EXISTS enigma.fn_leveling_script_ejecutado(sql_level_version VARCHAR(16))
RETURNS DATETIME
COMMENT 'Informa si el script con versión de nivelado de SQL informado ha sido ejecutado'
BEGIN
	return (SELECT sli_script_fecha_ejecucion FROM enigma.sql_leveling_info WHERE sli_script_version = sql_level_version );
END$$
DELIMITER ;

DELIMITER $$
$$
CREATE PROCEDURE IF NOT EXISTS enigma.sp_leveling_script_ejecutado(sql_level_version VARCHAR(16))
COMMENT 'Registra la ejecución correcta de script de nivelado con la versión indicada'
BEGIN
	INSERT INTO enigma.sql_leveling_info(sli_script_version, sli_script_fecha_ejecucion, sli_script_resultado)
		VALUES (sql_level_version, NOW(), 'OK');
END$$
DELIMITER ;

DELIMITER $$
$$ BEGIN NOT ATOMIC
	SET @script_version = '0.0.1';
	SELECT @ejecutado := enigma.fn_leveling_script_ejecutado(@script_version);
	SELECT @errmsg := CONCAT('SCRIPT ', @script_version, ' EJECUTADO EN FECHA ', @ejecutado);

	IF @ejecutado
		THEN
			SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT = @errmsg;
		ELSE 
			CALL enigma.sp_leveling_script_ejecutado(@script_version);
			SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ' , @script_version ) estado_ejecucion;
	END IF;
	END$$
DELIMITER ;