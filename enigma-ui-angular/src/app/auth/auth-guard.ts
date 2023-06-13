import { inject } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
} from '@angular/router';
import { AuthService } from './auth.service';

export const canActivateAuth: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  _: RouterStateSnapshot
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
