CREATE TABLE enigma.usuarios_validacion_email (
	uve_id bigint auto_increment NOT NULL,
	uve_usu_id bigint NOT NULL,
	uve_fecha_alta datetime NOT NULL,
	uve_fecha_baja datetime NOT NULL,
	uve_salt varchar(64) NOT NULL,
	uve_validado bool DEFAULT 0 NOT NULL,
	uve_nuevo_correo VARCHAR(100) NOT NULL,
	CONSTRAINT usuarios_validacion_email_PK PRIMARY KEY (uve_id),
	CONSTRAINT usuarios_validacion_email_FK FOREIGN KEY (uve_usu_id) REFERENCES enigma.usuarios(usu_id)
);

CREATE INDEX usuarios_validacion_email_uve_salt_IDX USING BTREE ON enigma.usuarios_validacion_email (uve_id, uve_salt);

DELIMITER $$
$$ CREATE TRIGGER TG_USUARIOS_VALIDACION_EMAIL_UPD
BEFORE UPDATE
ON enigma.usuarios_validacion_email FOR EACH ROW
BEGIN
 	IF (new.uve_validado) THEN 
		UPDATE enigma.usuarios
			SET usu.usu_correo = new.uve_nuevo_correo,
			usu.usu_correo_validado = TRUE
		WHERE usu.usu_id = new.uve_usu_id;
	END IF;
	SET NEW.uve_fecha_baja = NOW();
END;
$$
DELIMITER ;