#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Boid群れシミュレーション実験結果の分析スクリプト

実験条件:
- A1, A2: 個体数の影響（A3が抜けている可能性）
- B1, B2, B3: 結合力の影響
- C1, C2, C3: 混乱強度の影響
"""

import pandas as pd
import matplotlib.pyplot as plt
import numpy as np
from pathlib import Path
import re

# 日本語フォント設定（macOS）
plt.rcParams['font.family'] = ['Hiragino Sans', 'Arial Unicode MS', 'sans-serif']

# データフォルダ
DATA_DIR = Path(__file__).parent.parent / "Assets" / "ExperimentData"


def load_all_summaries():
    """全てのsummary.txtを読み込んでDataFrameにする"""
    summaries = []
    for f in DATA_DIR.glob("*_summary.txt"):
        data = {}
        with open(f, 'r') as fp:
            for line in fp:
                if ':' in line and '===' not in line:
                    key, val = line.split(':', 1)
                    key = key.strip()
                    val = val.strip().replace('s', '').replace(' per minute', '')
                    data[key] = val
        if data:
            summaries.append(data)
    
    df = pd.DataFrame(summaries)
    # 数値変換
    for col in ['Initial Boid Count', 'Total Duration', 'Total Kills', 'Capture Rate']:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors='coerce')
    if 'First Kill Time' in df.columns:
        df['First Kill Time'] = pd.to_numeric(df['First Kill Time'].str.replace('N/A', ''), errors='coerce')
    return df


def load_all_captures():
    """全てのcaptures.csvを結合する"""
    dfs = []
    for f in DATA_DIR.glob("*_captures.csv"):
        try:
            df = pd.read_csv(f)
            dfs.append(df)
        except Exception as e:
            print(f"Error loading {f}: {e}")
    if dfs:
        return pd.concat(dfs, ignore_index=True)
    return pd.DataFrame()


def load_all_snapshots():
    """全てのsnapshots.csvを結合する"""
    dfs = []
    for f in DATA_DIR.glob("*_snapshots.csv"):
        try:
            df = pd.read_csv(f)
            dfs.append(df)
        except Exception as e:
            print(f"Error loading {f}: {e}")
    if dfs:
        return pd.concat(dfs, ignore_index=True)
    return pd.DataFrame()


def plot_condition_comparison(summaries):
    """条件間の比較グラフ"""
    fig, axes = plt.subplots(1, 3, figsize=(15, 5))
    
    df = summaries.sort_values('Condition')
    
    # 1. 最初の捕食までの時間
    ax = axes[0]
    colors = ['#e74c3c' if c.startswith('A') else '#3498db' if c.startswith('B') else '#2ecc71' 
              for c in df['Condition']]
    ax.bar(df['Condition'], df['First Kill Time'], color=colors)
    ax.set_ylabel('First Kill Time (sec)')
    ax.set_xlabel('Condition')
    ax.set_title('最初の捕食までの時間')
    ax.axhline(df['First Kill Time'].mean(), color='gray', linestyle='--', alpha=0.5, label=f'Mean: {df["First Kill Time"].mean():.1f}s')
    ax.legend()
    
    # 2. 時間当たり捕食数
    ax = axes[1]
    ax.bar(df['Condition'], df['Capture Rate'], color=colors)
    ax.set_ylabel('Capture Rate (kills/min)')
    ax.set_xlabel('Condition')
    ax.set_title('時間当たり捕食数')
    ax.axhline(df['Capture Rate'].mean(), color='gray', linestyle='--', alpha=0.5, label=f'Mean: {df["Capture Rate"].mean():.1f}/min')
    ax.legend()
    
    # 3. 実験時間（12匹捕食までの時間）
    ax = axes[2]
    ax.bar(df['Condition'], df['Total Duration'], color=colors)
    ax.set_ylabel('Total Duration (sec)')
    ax.set_xlabel('Condition')
    ax.set_title('12匹捕食までの時間')
    
    plt.tight_layout()
    plt.savefig(DATA_DIR.parent.parent / 'ExperimentData' / 'comparison_conditions.png', dpi=150)
    plt.show()
    print("Saved: comparison_conditions.png")


def plot_predation_timeline(captures):
    """捕食の時系列グラフ（条件別）"""
    fig, ax = plt.subplots(figsize=(12, 6))
    
    conditions = sorted(captures['condition'].unique())
    colors = plt.cm.Set2(np.linspace(0, 1, len(conditions)))
    
    for cond, color in zip(conditions, colors):
        df = captures[captures['condition'] == cond]
        ax.plot(df['time_sec'], df['kill_number'], marker='o', label=cond, color=color, linewidth=2)
    
    ax.set_xlabel('Time (sec)')
    ax.set_ylabel('Cumulative Kills')
    ax.set_title('捕食の時系列比較')
    ax.legend(title='Condition')
    ax.grid(True, alpha=0.3)
    
    plt.tight_layout()
    plt.savefig(DATA_DIR.parent.parent / 'ExperimentData' / 'predation_timeline.png', dpi=150)
    plt.show()
    print("Saved: predation_timeline.png")


def plot_confusion_effect(captures):
    """視野内の個体数と捕食間隔の関係（Confusion Effect の検証）"""
    fig, axes = plt.subplots(1, 2, figsize=(12, 5))
    
    # 捕食間隔を計算
    captures = captures.copy()
    captures['time_diff'] = captures.groupby('condition')['time_sec'].diff()
    
    # 1. 視野内個体数のヒストグラム
    ax = axes[0]
    ax.hist(captures['boids_in_view'], bins=20, edgecolor='black', alpha=0.7)
    ax.set_xlabel('Boids in View (at capture)')
    ax.set_ylabel('Frequency')
    ax.set_title('捕食時の視野内個体数の分布')
    ax.axvline(captures['boids_in_view'].median(), color='red', linestyle='--', 
               label=f'Median: {captures["boids_in_view"].median():.0f}')
    ax.legend()
    
    # 2. 視野内個体数 vs 捕食間隔
    ax = axes[1]
    valid = captures.dropna(subset=['time_diff'])
    ax.scatter(valid['boids_in_view'], valid['time_diff'], alpha=0.6)
    ax.set_xlabel('Boids in View')
    ax.set_ylabel('Time to Next Capture (sec)')
    ax.set_title('視野内個体数 vs 次の捕食までの時間')
    
    # 回帰線
    if len(valid) > 2:
        z = np.polyfit(valid['boids_in_view'], valid['time_diff'], 1)
        p = np.poly1d(z)
        x_line = np.linspace(valid['boids_in_view'].min(), valid['boids_in_view'].max(), 100)
        ax.plot(x_line, p(x_line), 'r--', alpha=0.5, label=f'Trend (slope={z[0]:.3f})')
        ax.legend()
    
    plt.tight_layout()
    plt.savefig(DATA_DIR.parent.parent / 'ExperimentData' / 'confusion_effect.png', dpi=150)
    plt.show()
    print("Saved: confusion_effect.png")


def plot_experiment_groups(summaries):
    """実験グループ別の比較（A, B, C）"""
    fig, axes = plt.subplots(1, 3, figsize=(15, 5))
    
    groups = {'A': '個体数の影響', 'B': '結合力の影響', 'C': '混乱強度の影響'}
    
    for i, (prefix, title) in enumerate(groups.items()):
        ax = axes[i]
        df = summaries[summaries['Condition'].str.startswith(prefix)].sort_values('Condition')
        if len(df) == 0:
            ax.text(0.5, 0.5, f'No data for {prefix}', ha='center', va='center')
            continue
        
        x = np.arange(len(df))
        width = 0.35
        
        bars1 = ax.bar(x - width/2, df['First Kill Time'], width, label='First Kill Time (s)', color='#3498db')
        ax2 = ax.twinx()
        bars2 = ax2.bar(x + width/2, df['Capture Rate'], width, label='Capture Rate (/min)', color='#e74c3c')
        
        ax.set_xlabel('Condition')
        ax.set_ylabel('First Kill Time (sec)', color='#3498db')
        ax2.set_ylabel('Capture Rate (kills/min)', color='#e74c3c')
        ax.set_xticks(x)
        ax.set_xticklabels(df['Condition'])
        ax.set_title(f'実験{prefix}: {title}')
        
        # 凡例
        lines1, labels1 = ax.get_legend_handles_labels()
        lines2, labels2 = ax2.get_legend_handles_labels()
        ax.legend(lines1 + lines2, labels1 + labels2, loc='upper right')
    
    plt.tight_layout()
    plt.savefig(DATA_DIR.parent.parent / 'ExperimentData' / 'experiment_groups.png', dpi=150)
    plt.show()
    print("Saved: experiment_groups.png")


def print_summary_table(summaries):
    """結果サマリーをテーブル表示"""
    df = summaries[['Condition', 'Initial Boid Count', 'First Kill Time', 'Capture Rate', 'Total Duration']].copy()
    df = df.sort_values('Condition')
    df.columns = ['条件', '初期個体数', '最初の捕食(秒)', '捕食率(/分)', '実験時間(秒)']
    
    print("\n" + "="*70)
    print("実験結果サマリー")
    print("="*70)
    print(df.to_string(index=False))
    print("="*70)
    
    return df


def write_analysis_report(summaries, captures):
    """分析レポートを出力"""
    output = DATA_DIR.parent.parent / 'ExperimentData' / 'analysis_report.md'
    
    df = summaries.sort_values('Condition')
    
    with open(output, 'w', encoding='utf-8') as f:
        f.write("# 実験結果分析レポート\n\n")
        
        f.write("## 1. 実験条件と結果\n\n")
        f.write("| 条件 | 初期個体数 | 最初の捕食(秒) | 捕食率(/分) | 実験時間(秒) |\n")
        f.write("|------|------------|----------------|-------------|-------------|\n")
        for _, row in df.iterrows():
            f.write(f"| {row['Condition']} | {row['Initial Boid Count']:.0f} | {row['First Kill Time']:.2f} | {row['Capture Rate']:.2f} | {row['Total Duration']:.2f} |\n")
        
        f.write("\n## 2. 統計サマリー\n\n")
        f.write(f"- 最初の捕食までの時間: 平均 {df['First Kill Time'].mean():.2f}秒, 標準偏差 {df['First Kill Time'].std():.2f}秒\n")
        f.write(f"- 時間当たり捕食数: 平均 {df['Capture Rate'].mean():.2f}/分, 標準偏差 {df['Capture Rate'].std():.2f}/分\n")
        
        f.write("\n## 3. 実験グループ別の傾向\n\n")
        
        for prefix, name in [('A', '個体数'), ('B', '結合力'), ('C', '混乱強度')]:
            group = df[df['Condition'].str.startswith(prefix)]
            if len(group) > 0:
                f.write(f"### 実験{prefix}: {name}の影響\n\n")
                f.write(f"- 条件数: {len(group)}\n")
                f.write(f"- 最初の捕食時間: {group['First Kill Time'].min():.2f}〜{group['First Kill Time'].max():.2f}秒\n")
                f.write(f"- 捕食率: {group['Capture Rate'].min():.2f}〜{group['Capture Rate'].max():.2f}/分\n\n")
        
        f.write("\n## 4. Confusion Effect の検証\n\n")
        if 'boids_in_view' in captures.columns:
            median_view = captures['boids_in_view'].median()
            f.write(f"- 捕食時の視野内個体数（中央値）: {median_view:.0f}匹\n")
            f.write(f"- 視野内個体数の範囲: {captures['boids_in_view'].min():.0f}〜{captures['boids_in_view'].max():.0f}匹\n")
        
        f.write("\n## 5. 考察\n\n")
        f.write("### 5.1 全体的な傾向\n\n")
        
        # 条件間の違いを分析
        fastest = df.loc[df['Capture Rate'].idxmax()]
        slowest = df.loc[df['Capture Rate'].idxmin()]
        
        f.write(f"- 最も捕食効率が高かった条件: **{fastest['Condition']}** ({fastest['Capture Rate']:.2f}/分)\n")
        f.write(f"- 最も捕食効率が低かった条件: **{slowest['Condition']}** ({slowest['Capture Rate']:.2f}/分)\n")
        f.write(f"- 効率の差: {fastest['Capture Rate'] - slowest['Capture Rate']:.2f}/分 ({(fastest['Capture Rate']/slowest['Capture Rate']-1)*100:.1f}%差)\n\n")
        
        f.write("### 5.2 Confusion Effect について\n\n")
        f.write("視野内の個体数が多いほど捕食者が混乱し、捕食効率が低下することが予測される。")
        f.write("実験データから、視野内個体数と捕食間隔の関係を分析することで、この効果を検証できる。\n\n")
        
        f.write("### 5.3 今後の課題\n\n")
        f.write("- 同一条件での複数回試行による再現性の確認\n")
        f.write("- パラメータの段階的な変更による詳細な影響分析\n")
        f.write("- 群れの密度・極性の時間変化と捕食成功率の関係分析\n")
    
    print(f"\nAnalysis report saved: {output}")


def main():
    print("Loading experiment data...")
    
    summaries = load_all_summaries()
    captures = load_all_captures()
    snapshots = load_all_snapshots()
    
    print(f"Loaded {len(summaries)} experiments")
    print(f"Conditions: {sorted(summaries['Condition'].unique())}")
    
    # サマリーテーブル表示
    print_summary_table(summaries)
    
    # グラフ生成
    print("\nGenerating plots...")
    plot_condition_comparison(summaries)
    plot_predation_timeline(captures)
    plot_confusion_effect(captures)
    plot_experiment_groups(summaries)
    
    # レポート生成
    write_analysis_report(summaries, captures)
    
    print("\nAnalysis complete!")


if __name__ == "__main__":
    main()
