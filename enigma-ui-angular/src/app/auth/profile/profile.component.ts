import { Store } from './../../core/stores/Store';
import { Component, OnInit, Signal } from '@angular/core';
import { FormControl, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../auth.service';
import { Profile } from '../model/profile';
import { PaisesStore } from 'src/app/core/stores/paises.store';
import { Pais } from 'src/app/core/model/pais';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatOptionModule } from '@angular/material/core';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCardModule } from '@angular/material/card';
import { NgIf, NgFor, AsyncPipe, UpperCasePipe } from '@angular/common';

@Component({
    selector: 'app-profile',
    templateUrl: './profile.component.html',
    styleUrls: ['./profile.component.scss'],
    standalone: true,
    imports: [
        NgIf,
        ReactiveFormsModule,
        MatCardModule,
        MatFormFieldModule,
        MatInputModule,
        MatAutocompleteModule,
        MatSnackBarModule,
        NgFor,
        MatOptionModule,
        MatButtonModule,
        MatIconModule,
        AsyncPipe,
        UpperCasePipe,
    ],
})
export class ProfileComponent implements OnInit {
  public perfil: Profile | undefined;
  public $paises: Signal<Store<Pais[]>> = this._countiesStore._store;

  perfilForm: FormGroup = new FormGroup({
    usuario: new FormControl('', []),
    correo: new FormControl('', []),
    nombre: new FormControl('', []),
    fechaNacimiento: new FormControl('', []),
    telefonoCodigoPais: new FormControl('', [Validators.pattern('[0-9]+')]),
    telefonoCodigoArea: new FormControl('', [Validators.pattern('[0-9]+')]),
    telefonoNumero: new FormControl('', [Validators.pattern('[0-9]+')]),
  });

  constructor(
    private _userService: AuthService,
    private _snackBar: MatSnackBar,
    private _countiesStore: PaisesStore
  ) {}

  async ngOnInit() {
    await this.cargarPerfil();
    await this._countiesStore.loadCountries();
  }

  async cargarPerfil() {
    var profile = await this._userService.getProfile();

    this.perfil = profile;
    this.perfilForm.patchValue(profile);

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
