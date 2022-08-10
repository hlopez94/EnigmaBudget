import { Directive, ElementRef } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth.service';

@Directive({
  selector: '[authIsLoggedIn]'
})
export class IsLoggedInDirective {


  constructor(private el: ElementRef<HTMLElement>, private authSvc: AuthService ) {}

  private async AuthIsLogged() {
    var isNotLoggedIn : boolean = await firstValueFrom(this.authSvc.IsUserLoggedIn);
      this.el.nativeElement.hidden= isNotLoggedIn
  }
}
