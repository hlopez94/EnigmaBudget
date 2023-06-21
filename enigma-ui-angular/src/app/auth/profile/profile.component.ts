import { Component, OnInit } from '@angular/core';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../auth.service';
import { Profile } from '../model/profile';
import { CountriesStore } from 'src/app/core/stores/countries.store';
import { Observable } from 'rxjs';
import { Pais } from 'src/app/core/model/pais';

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  styleUrls: ['./profile.component.scss'],
})
export class ProfileComponent implements OnInit {
  public perfil: Profile | undefined;
  readonly $paises: Observable<Pais[]> = this._countiesStore.countries;

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
    private _snackBar: MatSnackBar,
    private _countiesStore: CountriesStore
  ) {}

  async ngOnInit() {
    await this.cargarPerfil();
    await this._countiesStore.loadCountries();
  }

  async cargarPerfil() {
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
