import { Injectable, inject } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivate,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
} from '@angular/router';
import { AuthService } from './auth.service';

@Injectable()
export class AuthGuard implements CanActivate {
  constructor(private _authService: AuthService, private _router: Router) {}

  async canActivate(
    route: ActivatedRouteSnapshot,
    state: RouterStateSnapshot
  ): Promise<boolean> {
    if (this._authService.isUserLoggedInSync()) {
      var noVerificado: Boolean = this._authService.cuentaVerificada();
      if (!noVerificado) {
        debugger;
        this._router.navigate(['/unverified-account'], {
          queryParams: {
            origin: route.url,
            originParams: JSON.stringify(route.queryParams ?? ''),
          },
          queryParamsHandling: 'merge',
        });
        return false;
      }

      return true;
    } else {
      this._router.navigate(['/login'], {
        queryParams: {
          origin: route.url,
          originParams: JSON.stringify(route.queryParams ?? ''),
        },
        queryParamsHandling: 'merge',
      });

      return false;
    }
  }
}

export const canActivateAuth: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot
) => {
  var _authService = inject(AuthService);
  var _router = inject(Router);
  if (_authService.isUserLoggedInSync()) {
    var noVerificado: Boolean = _authService.cuentaVerificada();
    if (!noVerificado) {
      debugger;
      _router.navigate(['/unverified-account'], {
        queryParams: {
          origin: route.url,
          originParams: JSON.stringify(route.queryParams ?? ''),
        },
        queryParamsHandling: 'merge',
      });
      return false;
    }

    return true;
  } else {
    _router.navigate(['/login'], {
      queryParams: {
        origin: route.url,
        originParams: JSON.stringify(route.queryParams ?? ''),
      },
      queryParamsHandling: 'merge',
    });

    return false;
  }
};
