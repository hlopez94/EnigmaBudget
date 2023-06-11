SET @script_version = 'x.y.z';
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

CREATE TABLE enigma.NOMBRE_TABLA (
	...
)
COMMENT='...';

ALTER TABLE NOMBRE_TABLA ADD CONSTRAINT UNI_tabla_tab_row UNIQUE (tab_row);

CREATE INDEX IDX_tabla_tab_row ON enigma.NOMBRE_TABLA (usu_correo);

DELIMITER $$
$$
CREATE TRIGGER TG_NOMBRETABLA_[INS|UPD|DEL|...|DESCRIPTOR_ACCION]
BEFORE [INSERT|UPDATE] 
	ON enigma.NOMBRE_TABLA 
    FOR EACH ROW BEGIN 
        ...
	END;$$
DELIMITER ;	

CALL enigma.sp_leveling_script_ejecutado(@script_version);
SELECT CONCAT('BASE DE DATOS NIVELADA A VERSION ', @script_version);