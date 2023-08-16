export interface Profile {
  usuario: string;
  correo: string;
  nombre: string | null;
  fechaNacimiento: Date | null;
  telefonoCodigoPais: number | null;
  telefonoCodigoArea: number | null;
  telefonoCodigoNumero: number | null;
}
