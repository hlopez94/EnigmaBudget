import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../auth.service';
import { Pais } from '../model/pais';
import { Profile } from '../model/profile';

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.scss'],
})
export class ProfileComponent implements OnInit {
  public perfil: Profile | undefined;
  public paises: Pais[] | undefined;

  perfilForm: FormGroup = new FormGroup({
    idUnicoUsuario: new FormControl('', [Validators.required]),
    usuario: new FormControl('', [Validators.required]),
    correo: new FormControl('', [Validators.required]),
    nombre: new FormControl('', []),
    fechaNacimiento: new FormControl('', [Validators.required]),
    telefonoCodigoPais: new FormControl('', [Validators.pattern('[0-9]+')]),
    telefonoCodigoArea: new FormControl('', [Validators.pattern('[0-9]+')]),
    telefonoNumero: new FormControl('', [Validators.pattern('[0-9]+')]),
  });

  constructor(
    private _userService: AuthService,
    private _snackBar: MatSnackBar
  ) {}

  async ngOnInit() {
    this.cargarPerfil();
  }

  async cargarPerfil() {
    this.paises = await this._userService.paises();
    var profile = await this._userService.getProfile();

    this.perfil = profile;
    this.perfilForm.setValue(profile);

    if(profile.fechaNacimiento){
      console.log(profile.fechaNacimiento.toISOString())
      this.perfilForm.get('fechaNacimiento')!.setValue(profile.fechaNacimiento.toISOString().split('T')[0]);
    }
    this.perfilForm.disable();
    this.perfilForm.markAsPristine();
  }

  editar() {
    this.perfilForm.enable();
  }

  public _filter(value: string): Pais[] {
    const filterValue = value.toLowerCase();

    return this.paises!.filter((pais) =>
      Object.values(pais)
        .concat(',')
        .toString()
        .toLowerCase()
        .includes(filterValue.toLowerCase())
    );
  }

  async submit() {
    var res = await this._userService.updateProfile(
      this.perfilForm.value as Profile
    );
    if (res) {
      this._snackBar.open('Perfil actualizado correctamente', undefined, {
        duration: 1000,
      });
    } else {
      this._snackBar.open('Error al actualizar el perfil', undefined, {
        duration: 1000,
      });
    }
    this.cargarPerfil();
  }

  cambiarClave(){

  }
}
