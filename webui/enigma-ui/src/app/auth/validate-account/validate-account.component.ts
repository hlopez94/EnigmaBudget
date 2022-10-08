import { Component, OnInit } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiResponse } from 'src/app/core/model/ApiResponse';
import { AuthService } from '../auth.service';

@Component({
  selector: 'app-validate-account',
  templateUrl: './validate-account.component.html',
  styleUrls: ['./validate-account.component.scss']
})
export class ValidateAccountComponent implements OnInit {

  constructor(private _authService: AuthService,
    private _route: ActivatedRoute,
    private _router: Router,
    private _snackbar : MatSnackBar) { }

  async ngOnInit() {

      var params = await firstValueFrom(this._route.queryParams);
      console.log(params['mail-token'])
      console.log(params['token'])

      var mailToken = params['token'];

      if(mailToken){
        this.validarMail(mailToken);
      }

  }

  async validarMail(token: string){
    var res : ApiResponse<boolean> = await this._authService.verifyAccountMail(token);

    if(res.ok){
      this._authService.logout();
      var snackRef = this._snackbar.open('Cuenta Verificada Correctamente. Volve a iniciar Sesión 😉', 'Iniciar Sesión', { duration:3000})

      snackRef.onAction().subscribe(_ => {
        this._router.navigate(['login']);
      })
      this._router.navigate(['/']);
    }
  }
}
