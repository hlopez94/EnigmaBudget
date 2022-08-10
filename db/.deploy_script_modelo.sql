SET @script_version = 'X.Y.Z';
SELECT @ejecutado := enigma.fn_leveling_script_ejecutado(@script_version);

DELIMITER $$
$$
IF @ejecutado is not null
THEN 
	SIGNAL SQLSTATE '45000' SET MYSQL_ERRNO=30001, MESSAGE_TEXT = 'SCRIPT X.Y.Z YA HA SIDO EJECUTADO';
END IF;
$$
DELIMITER ;

CREATE TABLE enigma.NOMBRE_TABLA (
	...
)
COMMENT='...';

ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_correo UNIQUE (usu_correo);
ALTER TABLE usuarios ADD CONSTRAINT UNI_usuarios_usu_usuario UNIQUE (usu_usuario);

CREATE INDEX IDX_usuarios_usu_correo ON enigma.usuarios (usu_correo);
CREATE INDEX IDX_usuarios_usu_usuario ON enigma.usuarios (usu_usuario);


DELIMITER $$
$$
CREATE TRIGGER TG_NOMBRETABLA_[INS|UPD|DEL|...|DESCRIPTOR_ACCION]
BEFORE [INSERT|UPDATE] 
	ON enigma.NOMBRE_TABLA 
    FOR EACH ROW BEGIN 
        ...
	END;$$
DELIMITER ;
	
DELIMITER $$
$$

CALL enigma.sp_leveling_script_ejecutado(@script_version);
SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ', @script_version);