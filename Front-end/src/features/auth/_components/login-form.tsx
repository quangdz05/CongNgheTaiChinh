import {
  ArrowUpRight,
  BrainCircuit,
  CheckCircle2,
  LockKeyhole,
  Network,
  ShieldCheck,
} from "lucide-react";
import { useState } from "react";
import { login } from "../_services/services";
type User = { username: string; role: string };
export function Login({ onLogin }: { onLogin: (u: User) => void }) {
  const [name, setName] = useState("admin"),
    [password, setPassword] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  return (
    <div className="login-page">
      <div className="login-story">
        <div className="brand">
          <span className="brand-mark">
            <ShieldCheck size={28} />
          </span>
          <span>fraudshield</span>
        </div>
        <div>
          <div className="eyebrow">FINTECH RISK OPERATIONS</div>
          <h1>
            Hiểu rõ rủi ro.
            <br />
            Kiểm soát giao dịch.
          </h1>
          <p>
            Một không gian để phân tích, giải thích và kiểm tra các giao dịch
            đáng ngờ.
          </p>
          <div className="login-features">
            <span>
              <CheckCircle2 size={18} /> Luật nghiệp vụ có giải thích
            </span>
            <span>
              <Network size={18} /> Liên kết tài khoản và thiết bị
            </span>
            <span>
              <BrainCircuit size={18} /> Mô hình AI qua API độc lập
            </span>
          </div>
        </div>
        <small>Prototype học phần · Dữ liệu mô phỏng</small>
      </div>
      <div className="login-form-wrap">
        <form
          className="login-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setBusy(true);
            setError("");
            try {
              const r = await login(name, password);
              sessionStorage.setItem("fs-token", r.token);
              onLogin(r);
            } catch (e) {
              setError((e as Error).message);
            } finally {
              setBusy(false);
            }
          }}
        >
          <div className="login-lock">
            <LockKeyhole size={26} />
          </div>
          <h2>Chào mừng trở lại</h2>
          <p>Đăng nhập vào không gian kiểm soát rủi ro.</p>
          <label>
            Tên đăng nhập
            <input
              autoComplete="username"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </label>
          <label>
            Mật khẩu
            <input
              type="password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          {error && <div className="error">{error}</div>}
          <button className="button primary wide" disabled={busy}>
            {busy ? "Đang đăng nhập…" : "Đăng nhập"}
            <ArrowUpRight size={17} />
          </button>
          <p className="helper">
            Tài khoản demo Development: admin / Admin@123456
            <br />
            Analyst: analyst / Analyst@123456
          </p>
        </form>
      </div>
    </div>
  );
}
